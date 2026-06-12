# M10 — Immersive Player Redesign (Plan)

> **Written for Sonnet execution.** Each task is bite-sized: implement → write/extend the failing
> test → make it pass → run the gate → commit. **If anything in this plan does not match the real
> code (a signature, a member, a file location), STOP and report rather than guess.** The player
> surface is integration-heavy (libVLC airspace); the airspace facts in the Decision-log are
> load-bearing — do not "simplify" them away.

## Scope & the two user decisions that shape this

VideoShelf v2's final milestone makes the player feel like Windows **Movies & TV**: edge-to-edge
video, **auto-hiding** Fluent controls, a **polished scrubber with chapter markers + a seek-preview
thumbnail**, and it **folds in the two deferred v1 player threads** (PiP black-frame, seek-preview
off-screen decoder). Two locked user decisions:

1. **Host = inline overlay, FULL-WINDOW.** The player overlays the **entire** main window (over the
   title bar + top nav + sidebar) while playing — not just the content columns it covers today. The
   transport-bar **bleed** onto non-player views (Home/Browse/Search — reproduced on `main`, deferred
   through M8/M9) is fixed by **only realizing the `VideoView` in the visual tree while playing**.
2. **PiP = IN-WINDOW and DRAGGABLE.** Replace the separate always-on-top `MiniPlayerWindow` with a
   small **floating, draggable panel inside the main window** that hosts the **same** `VideoView`.
   Because the one libVLC `MediaPlayer` never moves to a second `VideoView`/window, the vout never
   re-hosts — **this is also the fix for the deferred PiP black-frame bug** (a re-host leaves the
   running vout bound to the old HWND → black mini-player). One `VideoView` for both full and PiP
   modes; PiP just shrinks + repositions + drag-enables it.

**Theming rule (caused regressions in a sibling project — non-negotiable):** never override or
re-base a WPF-UI themed control's `Style`/`ControlTemplate` for cosmetics. The "scrubber with
markers" is built **additively** — overlay elements layered *over* the stock `Slider`, never a
`Slider` template override. Chapter ticks/preview popup are sibling adorner elements on a `Canvas`,
not template parts.

**Out of scope (unchanged v1 §13):** playback-speed, external subtitle sidecars, whole-library
continuous play, online scraping, transcoding, casting, file deletion. **No new external tools** —
seek-preview frames come from bundled libVLC only.

**Two facts that constrain the UI (carry forward, do not re-discover):**
- `duration` is **never populated** app-wide (the scanner doesn't probe it), so any
  `duration`-derived progress is empty. The scrubber's range uses the **engine's live
  `LengthSeconds`** (from libVLC `LengthChanged`), which *is* populated — so the scrubber works
  regardless. Do not bind the scrubber to `duration`/`ProgressFraction`.
- **Buffered-region markers are N/A** for local-only playback (the whole local file is "buffered"),
  so "markers" here means **chapter ticks + the position thumb**, not a buffered bar. Don't fabricate
  a buffered region.

---

## Task 1 — Engine seam: off-screen seek-preview decoder + chapter start times

**Goal:** give the scrubber real data — chapter **start times** (to place tick marks) and a
**position-accurate** seek-preview frame (the deferred off-screen decoder) — behind the existing
`IPlaybackEngine` seam so the VM stays unit-testable.

### 1a. `IPlaybackEngine.cs` — add `StartSeconds` to `ChapterOption`
`src/VideoShelf.App/Services/IPlaybackEngine.cs`. Change the record (additive, defaulted so existing
construction sites keep compiling):

```csharp
/// <summary>An embedded chapter. Index is the libVLC chapter index (0-based); Name may be empty.
/// StartSeconds is the chapter's start offset in seconds (for scrubber tick marks; 0 if unknown).</summary>
public sealed record ChapterOption(int Index, string Name, double StartSeconds = 0);
```

### 1b. `LibVlcPlaybackEngine.cs` — populate chapter start times
`src/VideoShelf.App/Services/LibVlcPlaybackEngine.cs`, in `GetChapters()`. The libVLC
`ChapterDescription` exposes `TimeOffset` (milliseconds). Map it:

```csharp
var chapters = _player.FullChapterDescriptions();
if (chapters is not null)
    for (var i = 0; i < chapters.Length; i++)
        list.Add(new ChapterOption(i, chapters[i].Name ?? $"Chapter {i + 1}",
                                   chapters[i].TimeOffset / 1000.0));
```
> If `ChapterDescription` has no `TimeOffset` member in the pinned LibVLCSharp 3.9.7.1 (it should —
> it's `Int64 TimeOffset`), STOP and report; do not invent a different property.

### 1c. `LibVlcPlaybackEngine.cs` — real off-screen seek-preview decoder (best-effort + fail-safe fallback)
Replace the body of `TryGeneratePreviewFrameAsync` (which currently just snapshots the **live**
frame, ignoring `seconds`). Implement a dedicated **off-screen** `MediaPlayer` that seeks to
`seconds` and snapshots **that** frame, falling back to the live snapshot on any failure so the
feature can never regress or throw.

```csharp
private MediaPlayer? _previewPlayer;
private string? _previewMediaPath;

public async Task<bool> TryGeneratePreviewFrameAsync(double seconds, string outputPngPath, CancellationToken cancellationToken)
{
    try
    {
        if (await TryOffScreenPreviewAsync(seconds, outputPngPath, cancellationToken).ConfigureAwait(false))
            return true;
    }
    catch { /* fall through to live snapshot */ }

    // Fail-safe fallback: snapshot the live frame (previous behaviour). Always non-throwing.
    try { return TrySnapshot(outputPngPath); }
    catch { return false; }
}

/// <summary>Decodes the frame at <paramref name="seconds"/> on a hidden, audio-disabled MediaPlayer
/// (so the live playback is untouched) and snapshots it. Returns false (caller falls back) if a frame
/// can't be produced within a short budget.</summary>
private async Task<bool> TryOffScreenPreviewAsync(double seconds, string outputPngPath, CancellationToken ct)
{
    var srcPath = _player.Media?.Mrl;
    if (string.IsNullOrEmpty(srcPath)) return false;

    if (_previewPlayer is null)
    {
        _previewPlayer = new MediaPlayer(_libVlc) { Mute = true };
        _previewMediaPath = null;
    }
    if (_previewMediaPath != srcPath)
    {
        using var m = new Media(_libVlc, new Uri(srcPath), ":no-audio");
        _previewPlayer.Media = m;
        _previewMediaPath = srcPath;
    }

    if (!_previewPlayer.IsPlaying) _previewPlayer.Play();
    _previewPlayer.Time = (long)(seconds * 1000);

    // Poll briefly for a decoded frame; snapshot succeeds only once a vout has produced one.
    var deadline = DateTime.UtcNow.AddMilliseconds(700);
    while (DateTime.UtcNow < deadline)
    {
        ct.ThrowIfCancellationRequested();
        if (_previewPlayer.TakeSnapshot(0, outputPngPath, 0, 0)
            && File.Exists(outputPngPath) && new FileInfo(outputPngPath).Length > 0)
        {
            _previewPlayer.SetPause(true); // keep it parked, ready for the next hover
            return true;
        }
        await Task.Delay(50, ct).ConfigureAwait(false);
    }
    return false;
}
```
Dispose `_previewPlayer` in `Dispose()` (before `_player`):
```csharp
try { _previewPlayer?.Dispose(); } catch { }
```
> **STOP-and-report condition (conscious fallback is acceptable):** if, in the Task 5 sweep, the
> off-screen player can't produce a snapshot in this environment (some libVLC builds need a real
> vout/surface for `TakeSnapshot`), the fail-safe fallback keeps the live-frame behaviour and the
> milestone still ships the full scrubber + preview UX. **Do not** spend the task fighting headless
> libVLC — implement as above, verify in Task 5, and if off-screen yields nothing, **report it** and
> keep the fallback (this matches the M3/M6 deferrals). Either outcome is a PASS for this task; the
> seam and the position-accurate *intent* are what matter.

### 1d. `FakePlaybackEngine.cs` — make the preview seam testable
`tests/VideoShelf.App.Tests/TestSupport/FakePlaybackEngine.cs`. Record the requested seconds and
actually write a small file so `RequestSeekPreviewAsync`'s `File.Exists`/length check passes:

```csharp
public double? LastPreviewSeconds { get; private set; }

public Task<bool> TryGeneratePreviewFrameAsync(double seconds, string outputPngPath, CancellationToken cancellationToken)
{
    LastPreviewSeconds = seconds;
    if (SnapshotShouldFail) return Task.FromResult(false);
    try { System.IO.File.WriteAllBytes(outputPngPath, new byte[] { 0x89, 0x50, 0x4E, 0x47 }); } catch { }
    return Task.FromResult(true);
}
```
(Existing `ChapterOption` construction in tests stays valid via the defaulted `StartSeconds`.)

**Verify:** `dotnet test VideoShelf.slnx -c Release --nologo -v q` (0 failures — engine isn't
unit-tested; this just confirms the solution still builds + all green).
**Commit:** `M10: off-screen seek-preview decoder + chapter start times (engine seam)`.

---

## Task 2 — PlayerViewModel: scrub state, seek-preview wiring, auto-hide flag (+ tests)

**Goal:** all *logic* for drag-to-seek, the seek-preview, and control auto-hide lives in the VM
(unit-tested with `FakePlaybackEngine`); the View binds to it.

### 2a. New observable state on `PlayerViewModel`
`src/VideoShelf.App/ViewModels/PlayerViewModel.cs`. Add near the other `[ObservableProperty]` fields:

```csharp
/// <summary>The scrubber's bound value. Mirrors PositionSeconds during playback, but is user-driven
/// while IsScrubbing (so dragging the thumb doesn't fight the per-second position updates).</summary>
[ObservableProperty]
private double _scrubPosition;

[ObservableProperty]
private bool _isScrubbing;

/// <summary>Path to the current seek-preview frame (shown in the thumbnail popup while scrubbing); null = none.</summary>
[ObservableProperty]
private string? _seekPreviewPath;

/// <summary>Drives the auto-hiding overlay's visibility. The View shows controls on activity and hides
/// them after an idle delay while playing; both set this. Starts visible.</summary>
[ObservableProperty]
private bool _areControlsVisible = true;

/// <summary>When true, the View's auto-hide timer is suppressed (controls stay up). Set by the harness
/// so the screenshot sweep captures the transport; off in normal use.</summary>
public bool AutoHideSuppressed { get; set; }

private CancellationTokenSource? _previewCts;
```

### 2b. Keep `ScrubPosition` in sync only when NOT scrubbing
In `OnPositionChanged`, after `PositionSeconds = seconds;` add:
```csharp
if (!IsScrubbing) ScrubPosition = seconds;
```
Also set `ScrubPosition = 0;` and `SeekPreviewPath = null;` in `Open(...)` where `_lastSavedAt`/
`_length` are reset, so a new episode starts the scrubber at 0.

### 2c. Scrub commands
Add these members (the View calls `BeginScrub` on drag-start, `UpdateScrubPreviewAsync` on drag-delta,
`CommitScrub` on drag-complete):

```csharp
/// <summary>Begins a scrub gesture: freezes ScrubPosition from playback updates.</summary>
public void BeginScrub() => IsScrubbing = true;

/// <summary>Loads (debounced, cancellable) the seek-preview frame for the scrubbed position.</summary>
public async Task UpdateScrubPreviewAsync(double seconds)
{
    _previewCts?.Cancel();
    var cts = _previewCts = new CancellationTokenSource();
    try
    {
        await Task.Delay(60, cts.Token).ConfigureAwait(true); // debounce rapid drag
        var path = await RequestSeekPreviewAsync(seconds, cts.Token).ConfigureAwait(true);
        if (!cts.Token.IsCancellationRequested) SeekPreviewPath = path;
    }
    catch (OperationCanceledException) { /* superseded by a newer hover */ }
}

/// <summary>Commits the scrub: seeks the engine to ScrubPosition and ends the gesture.</summary>
public void CommitScrub()
{
    engine.SeekTo(ScrubPosition);
    PositionSeconds = ScrubPosition;
    IsScrubbing = false;
    SeekPreviewPath = null;
    _previewCts?.Cancel();
    CanResume = false; // a manual seek dismisses the resume offer
}
```

Update the cache key in `RequestSeekPreviewAsync` now that the frame is position-accurate (Task 1):
cache per rounded second instead of overwriting one file, and skip regen if present —
```csharp
var target = Path.Combine(SeekPreviewDirectory, $"preview_{(int)Math.Round(seconds)}.png");
if (File.Exists(target) && new FileInfo(target).Length > 0) return target;
```
and pass `seconds` through to `engine.TryGeneratePreviewFrameAsync(seconds, target, ...)` (already
does). Update the method's `<remarks>` to drop the "ignores seconds / don't cache" note (no longer true).

### 2d. Tests — `tests/VideoShelf.App.Tests/PlayerViewModelTests.cs`
Add (reuse the existing `Seed()` + `NewVm(...)` helpers):

1. `Scrubbing_freezes_ScrubPosition_from_position_updates`: `vm.BeginScrub(); vm.ScrubPosition = 42;`
   then `engine.RaisePosition(5);` → `vm.ScrubPosition` stays `42` and `vm.IsScrubbing` is true.
2. `Not_scrubbing_ScrubPosition_tracks_playback`: `engine.RaiseLength(100); engine.RaisePosition(30);`
   → `vm.ScrubPosition == 30`.
3. `CommitScrub_seeks_engine_to_scrub_position_and_ends_gesture`: `vm.BeginScrub(); vm.ScrubPosition = 55;
   vm.CommitScrub();` → `engine.Seeks` last == `55`, `vm.IsScrubbing` false, `vm.SeekPreviewPath` null.
4. `UpdateScrubPreviewAsync_passes_position_to_engine_and_sets_path`: `await vm.UpdateScrubPreviewAsync(33);`
   → `engine.LastPreviewSeconds == 33` and `vm.SeekPreviewPath` is non-null (Fake writes a file).
   (Point `vm.SeekPreviewDirectory` at a temp dir in the test.)
5. `UpdateScrubPreviewAsync_returns_null_path_when_engine_fails`: set `engine.SnapshotShouldFail = true;`
   → `vm.SeekPreviewPath` is null.

**Verify:** gate green, new tests pass.
**Commit:** `M10: scrubber + seek-preview VM logic with tests`.

---

## Task 3 — Immersive `PlayerView` (edge-to-edge, auto-hide, polished scrubber, draggable PiP)

**Goal:** rebuild `PlayerView` as the immersive surface. Single `VideoView`; two visual modes (full /
floating PiP) driven by `DataContext.IsPictureInPicture`; auto-hiding Fluent controls; a polished,
**additively-decorated** scrubber with chapter ticks + a seek-preview thumbnail popup; drag-to-seek;
and (for PiP) a draggable floating panel. **No `Slider` template override.**

Files: `src/VideoShelf.App/Views/PlayerView.xaml` + `PlayerView.xaml.cs`.

### 3a. `PlayerView.xaml` — full rewrite of the body
Keep the `UserControl` header (the `DesignTokens` merge + `BoolToVisibility`). Replace the `<Grid>`
body with this structure. Notes inline; **bindings reference `Player.*` and the `MainViewModel`
commands exactly as today** (`DataContext` is the `MainViewModel`).

```xml
<!-- RootGrid is hit-transparent in PiP (Background=null) so clicks fall through to the views behind;
     opaque black in full mode so the immersive player covers everything. The PlayerShell border is
     the actual player; in PiP it shrinks to a floating, draggable panel anchored bottom-right. -->
<Grid x:Name="RootGrid" Background="Black"
      MouseMove="OnSurfaceMouseMove">
    <Border x:Name="PlayerShell" Background="Black">
        <Border.RenderTransform>
            <TranslateTransform x:Name="PipTranslate" X="0" Y="0" />
        </Border.RenderTransform>

        <vlc:VideoView x:Name="VideoSurface">
            <Grid x:Name="OverlayRoot">

                <!-- Error / missing-file banner (unchanged, additive) -->
                <Border VerticalAlignment="Center" HorizontalAlignment="Center"
                        Padding="20" CornerRadius="8" Background="#CC202020"
                        Visibility="{Binding Player.HasError, Converter={StaticResource BoolToVisibility}}">
                    <TextBlock Text="{Binding Player.PlaybackError}" Foreground="White"
                               TextAlignment="Center" TextWrapping="Wrap" MaxWidth="480" />
                </Border>

                <!-- Resume-offer banner (unchanged, additive) -->
                <Border VerticalAlignment="Top" HorizontalAlignment="Center" Margin="0,16,0,0"
                        Padding="14,8" CornerRadius="6" Background="#CC202020"
                        Visibility="{Binding Player.CanResume, Converter={StaticResource BoolToVisibility}}">
                    <StackPanel Orientation="Horizontal">
                        <TextBlock Text="Resume where you left off?" Foreground="White"
                                   VerticalAlignment="Center" Margin="0,0,12,0" />
                        <ui:Button Content="Resume" Command="{Binding Player.ResumeCommand}" />
                    </StackPanel>
                </Border>

                <!-- ===== Auto-hiding controls layer ===== -->
                <Grid x:Name="ControlsLayer"
                      Visibility="{Binding Player.AreControlsVisible, Converter={StaticResource BoolToVisibility}}">

                    <!-- Top scrim + title/close (drag strip in PiP) -->
                    <Border x:Name="TopBar" VerticalAlignment="Top" Padding="12,8"
                            Background="#80101010" MouseLeftButtonDown="OnTopBarMouseDown">
                        <Grid>
                            <TextBlock Text="{Binding Player.Title}" Foreground="White"
                                       VerticalAlignment="Center" FontSize="14"
                                       TextTrimming="CharacterEllipsis" Margin="0,0,160,0" />
                            <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
                                <!-- 'Back to window' only in PiP -->
                                <ui:Button x:Name="BackToWindowButton" Content="Back to window"
                                           Command="{Binding TogglePictureInPictureCommand}"
                                           Visibility="Collapsed" Margin="0,0,8,0" />
                                <ui:Button Content="Close" Command="{Binding ClosePlayerCommand}" />
                            </StackPanel>
                        </Grid>
                    </Border>

                    <!-- Seek-preview thumbnail popup (shown while scrubbing) -->
                    <Border x:Name="SeekPreview" Width="160" Height="90"
                            VerticalAlignment="Bottom" HorizontalAlignment="Left"
                            Margin="0,0,0,86" CornerRadius="6" Background="#E0101010"
                            BorderBrush="#60FFFFFF" BorderThickness="1"
                            Visibility="{Binding Player.IsScrubbing, Converter={StaticResource BoolToVisibility}}">
                        <Image Source="{Binding Player.SeekPreviewPath}" Stretch="UniformToFill" />
                    </Border>

                    <!-- Bottom transport -->
                    <Border VerticalAlignment="Bottom" Background="#B0101010" Padding="12,8">
                        <StackPanel>
                            <!-- Scrubber row: a Grid layers chapter ticks (a Canvas) UNDER the stock
                                 Slider — additive, no template override. -->
                            <Grid>
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="Auto" />
                                    <ColumnDefinition Width="*" />
                                    <ColumnDefinition Width="Auto" />
                                </Grid.ColumnDefinitions>
                                <TextBlock Grid.Column="0" Foreground="White" VerticalAlignment="Center"
                                           Text="{Binding Player.PositionSeconds, StringFormat={}{0:0}s}" Margin="0,0,8,0" />
                                <Grid Grid.Column="1">
                                    <!-- Chapter tick marks, positioned in code-behind from ChapterOption.StartSeconds -->
                                    <Canvas x:Name="ChapterTicks" Height="6" VerticalAlignment="Bottom"
                                            Margin="0,0,0,2" IsHitTestVisible="False" />
                                    <Slider x:Name="SeekBar" VerticalAlignment="Center"
                                            Minimum="0" Maximum="{Binding Player.LengthSeconds}"
                                            Value="{Binding Player.ScrubPosition, Mode=TwoWay}" />
                                </Grid>
                                <TextBlock Grid.Column="2" Foreground="White" VerticalAlignment="Center"
                                           Text="{Binding Player.LengthSeconds, StringFormat={}{0:0}s}" Margin="8,0,0,0" />
                            </Grid>

                            <StackPanel Orientation="Horizontal" Margin="0,8,0,0">
                                <ui:Button Content="Play/Pause" Command="{Binding Player.TogglePlayPauseCommand}" />
                                <ui:Button Content="&#9664; Chapter" Margin="8,0,0,0"
                                           Command="{Binding Player.PreviousChapterCommand}"
                                           Visibility="{Binding Player.HasChapters, Converter={StaticResource BoolToVisibility}}" />
                                <ui:Button Content="Chapter &#9654;" Margin="4,0,0,0"
                                           Command="{Binding Player.NextChapterCommand}"
                                           Visibility="{Binding Player.HasChapters, Converter={StaticResource BoolToVisibility}}" />
                                <ComboBox Margin="8,0,0,0" Width="140"
                                          ItemsSource="{Binding Player.AudioTracks}"
                                          SelectedItem="{Binding Player.SelectedAudioTrack}"
                                          DisplayMemberPath="Label"
                                          Visibility="{Binding Player.HasMultipleAudioTracks, Converter={StaticResource BoolToVisibility}}" />
                                <ComboBox Margin="8,0,0,0" Width="140"
                                          ItemsSource="{Binding Player.SubtitleTracks}"
                                          SelectedItem="{Binding Player.SelectedSubtitleTrack}"
                                          DisplayMemberPath="Label"
                                          Visibility="{Binding Player.HasSubtitleTracks, Converter={StaticResource BoolToVisibility}}" />
                                <Slider Margin="12,0,0,0" Width="100" VerticalAlignment="Center"
                                        Minimum="0" Maximum="100" Value="{Binding Player.Volume}" />
                                <ui:Button Content="Screenshot" Margin="8,0,0,0" Command="{Binding Player.ScreenshotCommand}" />
                                <ui:Button Content="Fullscreen" Margin="8,0,0,0" Command="{Binding Player.ToggleFullscreenCommand}" />
                                <ui:Button Content="Mini-player" Margin="8,0,0,0" Command="{Binding TogglePictureInPictureCommand}" />
                            </StackPanel>
                        </StackPanel>
                    </Border>
                </Grid>
            </Grid>
        </vlc:VideoView>
    </Border>

    <!-- ===== PiP visual state: shrink + anchor + drag-enable PlayerShell, make RootGrid click-through ===== -->
    <Grid.Style>
        <Style TargetType="Grid">
            <Style.Triggers>
                <DataTrigger Binding="{Binding IsPictureInPicture}" Value="True">
                    <Setter Property="Background" Value="{x:Null}" />
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </Grid.Style>
</Grid>
```

Add the PiP sizing/anchor to `PlayerShell` via its own style triggers (kept additive — only layout
props, never a template):
```xml
<!-- Put this inside <Border x:Name="PlayerShell" ...> as PlayerShell.Style -->
<Border.Style>
    <Style TargetType="Border">
        <Setter Property="HorizontalAlignment" Value="Stretch" />
        <Setter Property="VerticalAlignment" Value="Stretch" />
        <Style.Triggers>
            <DataTrigger Binding="{Binding IsPictureInPicture}" Value="True">
                <Setter Property="HorizontalAlignment" Value="Left" />
                <Setter Property="VerticalAlignment" Value="Top" />
                <Setter Property="Width" Value="360" />
                <Setter Property="Height" Value="203" />
                <Setter Property="CornerRadius" Value="8" />
            </DataTrigger>
        </Style.Triggers>
    </Style>
</Border.Style>
```
> PiP anchors **top-left + a `TranslateTransform`** (not bottom-right) because the drag math updates
> `PipTranslate.X/Y` from a 0,0 origin; the code-behind seeds the initial offset to the bottom-right
> on entering PiP (3b). HwndHost airspace note: the floating video's corners won't visually clip to
> `CornerRadius` (the native child HWND is rectangular) — **acceptable**, do not try to round the
> video itself.

### 3b. `PlayerView.xaml.cs` — auto-hide, drag-to-seek, chapter ticks, PiP drag
Replace the code-behind. Wire: Loaded/Unloaded attach-detach; a `DispatcherTimer` auto-hide; the
`SeekBar` `Thumb` drag handlers (drag-to-seek + preview); chapter tick rendering when length/chapters
change; PiP enter/exit layout + dragging the floating panel; keep the existing `OnKeyDown` map.

```csharp
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Shapes;
using System.Windows.Media;
using System.Windows.Threading;
using VideoShelf.App.Services;
using VideoShelf.App.ViewModels;

namespace VideoShelf.App.Views;

public partial class PlayerView : UserControl
{
    private readonly DispatcherTimer _autoHide;
    private MainViewModel? _main;

    public PlayerView()
    {
        InitializeComponent();
        _autoHide = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _autoHide.Tick += OnAutoHideTick;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        KeyDown += OnKeyDown;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachSurface();
        _main = DataContext as MainViewModel;
        if (_main is not null)
        {
            _main.Player.RefreshTracks();
            _main.Player.PropertyChanged += OnPlayerPropertyChanged;
            _main.PropertyChanged += OnMainPropertyChanged;
        }

        // Wire drag-to-seek once the Slider's Thumb template part exists.
        SeekBar.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(OnSeekDragStarted));
        SeekBar.AddHandler(Thumb.DragDeltaEvent, new DragDeltaEventHandler(OnSeekDragDelta));
        SeekBar.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(OnSeekDragCompleted));

        ApplyPipState();
        RenderChapterTicks();
        ShowControls();
        Focus();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _autoHide.Stop();
        if (_main is not null)
        {
            _main.Player.PropertyChanged -= OnPlayerPropertyChanged;
            _main.PropertyChanged -= OnMainPropertyChanged;
        }
        DetachSurface();   // destroying this control tears down the VideoView + its overlay HWND
    }

    /// <summary>Binds the shared libVLC MediaPlayer to the VideoView (one VideoView for full + PiP).</summary>
    public void AttachSurface()
    {
        if (DataContext is MainViewModel main && main.Player.Engine is LibVlcPlaybackEngine vlc)
            VideoSurface.MediaPlayer = vlc.MediaPlayer;
    }

    public void DetachSurface() => VideoSurface.MediaPlayer = null;

    // ---- auto-hide ----
    private void OnSurfaceMouseMove(object sender, MouseEventArgs e) => ShowControls();

    private void ShowControls()
    {
        if (_main is not null) _main.Player.AreControlsVisible = true;
        _autoHide.Stop();
        _autoHide.Start();
    }

    private void OnAutoHideTick(object? sender, EventArgs e)
    {
        _autoHide.Stop();
        if (_main is null) return;
        // Keep controls up when suppressed (harness), paused, scrubbing, or showing a banner.
        if (_main.Player.AutoHideSuppressed || !_main.Player.IsPlaying ||
            _main.Player.IsScrubbing || _main.Player.HasError || _main.Player.CanResume)
            return;
        _main.Player.AreControlsVisible = false;
    }

    private void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PlayerViewModel.LengthSeconds) or nameof(PlayerViewModel.HasChapters))
            RenderChapterTicks();
    }

    private void OnMainPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsPictureInPicture))
            ApplyPipState();
    }

    // ---- drag-to-seek + preview ----
    private void OnSeekDragStarted(object sender, DragStartedEventArgs e)
    {
        _main?.Player.BeginScrub();
        ShowControls();
    }

    private async void OnSeekDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (_main is null) return;
        _autoHide.Stop();
        PositionSeekPreview(_main.Player.ScrubPosition, _main.Player.LengthSeconds);
        await _main.Player.UpdateScrubPreviewAsync(_main.Player.ScrubPosition);
    }

    private void OnSeekDragCompleted(object sender, DragCompletedEventArgs e)
    {
        _main?.Player.CommitScrub();
        ShowControls();
    }

    /// <summary>Slides the preview thumbnail horizontally to track the scrub position.</summary>
    private void PositionSeekPreview(double seconds, double length)
    {
        if (length <= 0 || SeekBar.ActualWidth <= 0) return;
        var frac = Math.Clamp(seconds / length, 0, 1);
        var x = SeekBar.TranslatePoint(new Point(0, 0), RootGrid).X
                + frac * SeekBar.ActualWidth - SeekPreview.Width / 2;
        SeekPreview.Margin = new Thickness(Math.Max(0, x), 0, 0, 86);
    }

    // ---- chapter ticks (additive overlay, no Slider template override) ----
    private void RenderChapterTicks()
    {
        ChapterTicks.Children.Clear();
        if (_main is null) return;
        var length = _main.Player.LengthSeconds;
        if (length <= 0 || SeekBar.ActualWidth <= 0) return;
        foreach (var ch in _main.Player.Chapters)
        {
            if (ch.StartSeconds <= 0 || ch.StartSeconds >= length) continue;
            var x = (ch.StartSeconds / length) * SeekBar.ActualWidth;
            var tick = new Rectangle { Width = 2, Height = 6, Fill = Brushes.White, Opacity = 0.7 };
            Canvas.SetLeft(tick, x);
            ChapterTicks.Children.Add(tick);
        }
    }

    // ---- PiP enter/exit + drag ----
    private void ApplyPipState()
    {
        if (_main is null) return;
        var pip = _main.IsPictureInPicture;
        BackToWindowButton.Visibility = pip ? Visibility.Visible : Visibility.Collapsed;
        if (pip)
        {
            // Seed the floating panel near the bottom-right of the window.
            PipTranslate.X = Math.Max(0, ActualWidth - 360 - 24);
            PipTranslate.Y = Math.Max(0, ActualHeight - 203 - 24);
        }
        else
        {
            PipTranslate.X = 0; PipTranslate.Y = 0;
        }
        ShowControls();
        Dispatcher.BeginInvoke(new Action(RenderChapterTicks), DispatcherPriority.Loaded);
    }

    private Point _dragStart;
    private bool _dragging;

    private void OnTopBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_main is null || !_main.IsPictureInPicture) return; // drag only in PiP
        _dragging = true;
        _dragStart = e.GetPosition(this);
        ((UIElement)sender).CaptureMouse();
        ((UIElement)sender).MouseMove += OnTopBarMouseMove;
        ((UIElement)sender).MouseLeftButtonUp += OnTopBarMouseUp;
        e.Handled = true;
    }

    private void OnTopBarMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var p = e.GetPosition(this);
        var dx = p.X - _dragStart.X;
        var dy = p.Y - _dragStart.Y;
        _dragStart = p;
        PipTranslate.X = Math.Clamp(PipTranslate.X + dx, 0, Math.Max(0, ActualWidth - 360));
        PipTranslate.Y = Math.Clamp(PipTranslate.Y + dy, 0, Math.Max(0, ActualHeight - 203));
    }

    private void OnTopBarMouseUp(object sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        ((UIElement)sender).ReleaseMouseCapture();
        ((UIElement)sender).MouseMove -= OnTopBarMouseMove;
        ((UIElement)sender).MouseLeftButtonUp -= OnTopBarMouseUp;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel main) return;
        ShowControls();
        var command = PlayerKeyMap.Resolve(e.Key, Keyboard.Modifiers);
        switch (command)
        {
            case PlayerCommand.TogglePlayPause: main.Player.TogglePlayPauseCommand.Execute(null); e.Handled = true; break;
            case PlayerCommand.SeekForward: main.Player.Engine.SeekTo(main.Player.PositionSeconds + 10); e.Handled = true; break;
            case PlayerCommand.SeekBackward: main.Player.Engine.SeekTo(main.Player.PositionSeconds - 10); e.Handled = true; break;
            case PlayerCommand.ToggleFullscreen: main.Player.ToggleFullscreenCommand.Execute(null); e.Handled = true; break;
            case PlayerCommand.ExitFullscreen: main.Player.IsFullscreen = false; e.Handled = true; break;
            case PlayerCommand.Screenshot: main.Player.ScreenshotCommand.Execute(null); e.Handled = true; break;
        }
    }
}
```
> The `Thumb.DragStarted/DragDelta/DragCompleted` routed events bubble from the `Slider`'s template
> Thumb — this is the standard, **template-safe** way to detect scrub start/end (no re-template). The
> `Value="{Binding Player.ScrubPosition, Mode=TwoWay}"` is settable (not the read-only-binding crash
> from M7) so TwoWay is correct here.

**Verify:** gate green (App builds; existing player tests unaffected). The view is integration-only —
screenshot-verified in Task 5.
**Commit:** `M10: immersive PlayerView — auto-hide controls, polished scrubber, seek-preview, draggable PiP`.

---

## Task 4 — Full-window host + airspace bleed fix; remove `MiniPlayerWindow`

**Goal:** (1) overlay the player over the **entire** window; (2) **kill the transport-bar bleed** by
only realizing `PlayerView` (and thus the `VideoView`) in the tree while playing; (3) delete the now
dead `MiniPlayerWindow` and its orchestration.

### 4a. `MainWindow.xaml` — move the player host to the outer grid, make it a dynamic `ContentControl`
Remove the current inline player from inside the main-content grid:
```xml
<!-- DELETE this block (currently the last child of the inner <Grid Grid.Row="1">): -->
<views:PlayerView x:Name="InlinePlayer"
                  Grid.Column="0" Grid.ColumnSpan="2"
                  DataContext="{Binding}"
                  Visibility="{Binding IsInlinePlayerVisible, Converter={StaticResource BoolToVisibility}}" />
```
Add a host that spans the **outer** grid's both rows (over the title bar + everything), as the **last
child of the outermost `<Grid>`** (sibling of `AppTitleBar` and the `Grid Grid.Row="1"`):
```xml
<!-- Immersive player host: overlays the ENTIRE window while playing. Content is set in code-behind
     only while playing, so the VideoView (and its airspace overlay HWND) is torn down when hidden —
     this is the fix for the transport-bar bleed onto non-player views. -->
<ContentControl x:Name="PlayerHost" Grid.Row="0" Grid.RowSpan="2" />
```
> Keep `IsInlinePlayerVisible`/`IsPlayerVisible`/`IsPictureInPicture` on `MainViewModel` as-is — the
> code-behind reads them. We no longer bind the host's `Visibility`; presence is driven by `Content`.

### 4b. `MainWindow.xaml.cs` — drive `PlayerHost.Content`; delete PiP-window orchestration
Replace the file's player/PiP logic:
- Delete the `_miniPlayer` field, `UpdatePictureInPicture(...)`, and the `IsPictureInPicture`
  subscription that called it.
- Add a single cached `PlayerView` and toggle it in/out of `PlayerHost` on `IsPlayerVisible`.
- Keep `UpdateFullscreen` (title-bar collapse) and its `IsFullscreen` subscription.

```csharp
private readonly MainViewModel _viewModel;
private PlayerView? _playerView;

public MainWindow(MainViewModel viewModel)
{
    InitializeComponent();
    _viewModel = viewModel;
    DataContext = viewModel;
    _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    _viewModel.Player.PropertyChanged += OnPlayerPropertyChanged;
    Loaded += async (_, _) =>
    {
        try { await _viewModel.InitializeAsync(); }
        catch { /* startup load is best-effort */ }
    };
}

private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
{
    if (e.PropertyName == nameof(MainViewModel.IsPlayerVisible))
        UpdatePlayerHost(_viewModel.IsPlayerVisible);
}

private void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
{
    if (e.PropertyName == nameof(PlayerViewModel.IsFullscreen))
        UpdateFullscreen(_viewModel.Player.IsFullscreen);
}

/// <summary>Realizes the player (and its VideoView) only while playing; tearing it down on hide
/// destroys the airspace overlay window that otherwise bleeds the transport bar onto other views.
/// PiP is an in-window mode of the SAME PlayerView — no separate window, so the vout never re-hosts.</summary>
private void UpdatePlayerHost(bool visible)
{
    if (visible)
    {
        _playerView ??= new PlayerView { DataContext = _viewModel };
        if (PlayerHost.Content is null) PlayerHost.Content = _playerView;
    }
    else
    {
        PlayerHost.Content = null; // Unloaded → DetachSurface + VideoView/overlay HWND destroyed
    }
}
```
Keep `UpdateFullscreen` exactly as today.
> The PiP toggle now needs **no** code-behind: `MainViewModel.TogglePictureInPicture` flips
> `IsPictureInPicture`, and `PlayerView.ApplyPipState` (Task 3) reacts. Confirm `MainViewModel`'s
> `ClosePlayer`/`TogglePictureInPicture` are unchanged and still compile.

### 4c. Delete `MiniPlayerWindow`
- Delete `src/VideoShelf.App/Views/MiniPlayerWindow.xaml` and `MiniPlayerWindow.xaml.cs`.
- Grep the solution for `MiniPlayerWindow` and remove any leftover references. **If a reference
  exists outside `MainWindow.xaml.cs` (e.g. DI, a test), STOP and report** before deleting — don't
  break an unexpected consumer. (Per the M6 notes it's instantiated only in `MainWindow.xaml.cs`.)
- It's superseded, recoverable via git — safe to remove.

**Verify:** gate green. App builds without `MiniPlayerWindow`.
**Commit:** `M10: full-window player host + airspace bleed fix; remove MiniPlayerWindow`.

---

## Task 5 — Harness, sweep, and screenshot verification

**Goal:** make the harness exercise the redesigned player + in-window PiP, then run the GDI sweep and
verify via a **Sonnet subagent text verdict** (never load PNGs into the controller).

### 5a. `HarnessRunner.cs` — keep controls up, render PiP in-window over content
In `PlayAsync(string clip, bool pip)`:
- Before `_main.PlayEpisode(episode);`, set `_main.Player.AutoHideSuppressed = true;` (so the sweep
  captures the transport instead of an auto-hidden frame).
- For `pip`: after `_main.IsPictureInPicture = true;`, set `_main.CurrentView = AppView.Home;` so the
  floating PiP panel is captured **over real content** (proves click-through + in-window placement).
  (The old comment about "triggers MiniPlayerWindow" is now wrong — update it to "in-window PiP".)

### 5b. `Run-VisualSweep.ps1` — no structural change needed
The `player` and `pip` entries already pass `--play`. Confirm they still work (the PiP shot now grabs
the **main** window — which contains the floating panel — exactly what `Capture-Window` already does;
there is no separate window to chase). No edit required unless the run shows otherwise.

### 5c. Run the sweep + verify (subagent text verdict)
Run `tools/harness/Run-VisualSweep.ps1` (needs an unlocked, composited desktop — the M6 gotchas
apply: ~5s Mica settle, TOPMOST→NOTOPMOST toggle, real interactive session or all-black). Then
dispatch **one Sonnet subagent** to Read the PNGs in the reported `PNG_DIR` and return a **PASS/FAIL
text verdict + the absolute paths it viewed**, against these acceptance criteria:

1. **`player.png` — immersive:** video is **edge-to-edge**; **no title bar / top nav / sidebar**
   visible (player covers the whole window). Auto-hiding transport is **visible** (controls up because
   `AutoHideSuppressed`). Scrubber shows a position thumb; chapter ticks appear **if** the clip has
   chapters (fixtures may have none — absence is not a FAIL).
2. **`player.png` — seek-preview seam:** not directly visible at rest (only during a live drag) — do
   **not** FAIL on its absence in a static shot. (The VM tests in Task 2 cover the wiring.)
3. **`pip.png` — in-window draggable PiP shows LIVE video:** a **small floating panel** (≈360×203,
   rounded chrome) over the Home content, **rendering actual video frames — NOT black** (this is the
   deferred black-frame fix). A "Back to window"/"Close" strip is visible on it; the Home rails are
   visible behind/around it (proves in-window + click-through).
4. **`home.png` / `browse.png` / `search.png` — NO bleed:** **no player transport bar** at the bottom
   of any non-player view (the M8/M9 bug is gone). This is the headline regression check.
5. **No regressions** on `section-detail.png` (hero + accordion still render; bottom rows no longer
   clipped by a bleeding transport bar) and the other views vs. the M9 baseline.

**If FAIL:** diagnose (common causes, in order): (a) bleed still present → the host isn't tearing down
`PlayerView` on hide (re-check `PlayerHost.Content = null` path + `Unloaded`→`DetachSurface`); (b) PiP
black → the floating panel re-created a VideoView or detached the MediaPlayer (it must be the **same**
VideoView, MediaPlayer never nulled in PiP); (c) controls hidden in the shot → `AutoHideSuppressed`
not set. Fix via the implementer loop and re-sweep until PASS. **Only** load a PNG into this session
if the user explicitly asks to see one.

**Commit (harness changes):** `M10: harness drives immersive player + in-window PiP for the sweep`.

---

## Finish (controller)

1. Final gate in the worktree: `dotnet test VideoShelf.slnx -c Release --nologo -v q` — **0 failures**
   (expect ~259 tests: 254 baseline + ~5 new PlayerViewModel scrub tests; Core unchanged at 118).
2. Final whole-branch review (fresh Sonnet) over `git diff main..HEAD`: airspace teardown correctness,
   no `MediaPlayer` re-host in PiP, theming-rule compliance (no `Slider` template override), no
   cross-thread `ObservableCollection` mutation.
3. Push `feat/immersive-player`; open the PR; **foreground** `gh run watch <id> --interval 20
   --exit-status` (sleep ~20s first); merge `--merge --delete-branch` **from the main repo root**;
   sync main; remove the worktree.
4. **Update `ROADMAP.md`:** flip the M10 row to `✅ Merged` with the PR # and a one-line summary; add
   an "M10 shipped" decision-log entry capturing the durable facts (the airspace teardown pattern =
   the real bleed fix; in-window single-VideoView PiP = the black-frame fix; off-screen decoder
   outcome — real vs. fallback; chapter-tick additive overlay; `AutoHideSuppressed` harness hook).
   Mark the two folded-in v1 threads (PiP black-frame, seek-preview decoder) **resolved**.
5. **Ping** the user (PushNotification): v2 (M7–M10) is complete — VideoShelf has no further
   `[ ] Not started` milestones, so the next phase is **scoping v3 or wrapping up**, not another build.

## Branch & commit conventions (from the runbook)
- Branch `feat/immersive-player`; worktree under `.worktrees/`. `gh` at
  `& "C:\Program Files\GitHub CLI\gh.exe"`. Merge `--merge` from the main repo root.
- Author `yovanmc`; trailer `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`. No Codex trailer.
- BOM-free commit-message files. One commit per task (5 commits).

## STOP-and-report triggers (don't guess)
- `ChapterDescription.TimeOffset` absent in pinned LibVLCSharp → stop (Task 1b).
- Off-screen `TakeSnapshot` yields no frame in-env → keep the fail-safe fallback + report; still PASS (Task 1c).
- `MiniPlayerWindow` referenced anywhere besides `MainWindow.xaml.cs` → stop before deleting (Task 4c).
- Sweep desktop locked/all-black, or any criterion can't be met after the documented fixes → stop and report.
- Any signature/member here not matching the real code → stop, don't substitute.
