# M9 — Creator page (Netflix-style) — implementation plan

> **Written for Sonnet execution.** Every task is bite-sized with exact files, complete
> code, exact commands, and expected output. **If something doesn't match what's described
> here (a signature, a file's structure, a missing method), STOP and report rather than
> guess.** Authored 2026-06-12 against a verbatim code digest; small drift is possible.

## Context & goal

VideoShelf v2 re-presents the library around **creators**. The existing **section-detail**
surface IS the creator page. M9 **redesigns it into a Netflix-style page** — most of the
functionality already exists (series list, tag editor, creator-art override, per-series
"Rename files…"); the new work is visual + two small VM additions.

**Locked design decisions (user, 2026-06-12, via one batched `AskUserQuestion`):**
1. **Art background = a top HERO BANNER.** The creator's resolved art (override → section
   seed frame) fills a banner across the top, dimmed with an additive gradient, with the
   creator name + "N videos" + applied tag pills + art-override buttons overlaid, fading
   into the series grid below. (Not a full-page background.)
2. **Series expansion = inline ACCORDION.** Series render as tiles in a responsive
   `WrapPanel` grid. Clicking a **multi-episode** tile toggles it expanded **in place**
   (episodes lazy-load on first expand: ▶ play + watched checkbox per episode). A
   **standalone** (single-episode) tile **plays on click** (no expand).
3. **Rename = preserved + folded in.** Every series tile gets a right-click **ContextMenu
   → "Rename files…"** (uniform, covers standalones too); multi-episode expanded panels also
   show a visible "Rename files…" button. This satisfies the v2 fold-in ("rename entry on
   creator surfaces") — rename is inherently per-series, so there is no creator-level rename;
   the creator page (reached from Browse on card click) is where it lives.

**Out of scope:** the pre-existing player-transport-bar bleed (deferred to M10; reproduced on
`main`); the immersive player; any Core schema change (M9 reuses existing repo methods).

### Conventions (from ROADMAP.md)

- Build: `dotnet build VideoShelf.slnx -c Release -v minimal`
- Test gate: `dotnet test VideoShelf.slnx -c Release --nologo -v q`
- `gh` not on PATH → `& "C:\Program Files\GitHub CLI\gh.exe"` (bash: `"/c/Program Files/GitHub CLI/gh.exe"`).
- Work in a worktree under `.worktrees/`; **direct pushes to `main` are blocked** — ship via branch + PR; merge `--merge` from the **main repo root**.
- Commit author `yovanmc` + trailer `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`. No Codex trailer.
- **Theming rule: additive only** — never re-base a WPF-UI control template for cosmetics. The hero dim is an additive `Border`+`LinearGradientBrush` over an `Image`, not a control re-template.
- **Cross-thread rule:** no `ConfigureAwait(false)` on a chain ending by mutating a UI-bound `ObservableCollection`.
- `RangeBase.Value`/`ProgressBar`/`Slider` → `Mode=OneWay` when bound to a read-only property.
- Converters (`BoolToVisibility`, `MissingToOpacity`, `EnumToVis`) resolve **app-level** (from `MainWindow.xaml`/`App.xaml`) — views reference them with `{StaticResource …}`; mirror the **existing `SectionDetailView.xaml`** header (it already uses these).

### Baseline

**244 tests (118 Core + 126 App), 0 failures.** M9 adds **App** tests only (no Core change expected); the count should rise.

---

## Known shapes (verbatim digest, captured 2026-06-12)

**`SectionDetailViewModel`** (`src/VideoShelf.App/ViewModels/SectionDetailViewModel.cs`):
```csharp
public sealed partial class SectionDetailViewModel(
    LibraryRepository library, TagRepository tags, WatchRepository watch,
    IThumbnailService thumbnails, CreatorArtRepository art, IImagePicker imagePicker) : ObservableObject
{
    public long SectionId { get; private set; }
    [ObservableProperty][NotifyPropertyChangedFor(nameof(HasCreatorArt))] private string? _creatorArtPath;
    public bool HasCreatorArt => !string.IsNullOrEmpty(CreatorArtPath);
    [ObservableProperty] private string _displayName = "";
    [ObservableProperty] private string _tagInput = "";
    public ObservableCollection<SeriesViewModel> SeriesList { get; } = [];
    public ObservableCollection<string> Tags { get; } = [];
    public ObservableCollection<string> Suggestions { get; } = [];
    public event EventHandler<EpisodeView>? PlayRequested;
    public event EventHandler<SeriesViewModel>? RenameRequested;
    public async Task LoadAsync(long sectionId) { … builds SeriesViewModels, loads tags, RefreshSuggestions(), RefreshCreatorArt() … }
    [RelayCommand] private void SetCreatorArt() { … imagePicker.PickImage(); art.SetArtPath(SectionId, picked); CreatorArtPath = picked; }
    [RelayCommand] private void ClearCreatorArt() { … art.ClearArtPath(SectionId); CreatorArtPath = null; }
    [RelayCommand] private void AddTag(); [RelayCommand] private void AddSuggestion(string tag); [RelayCommand] private void RemoveTag(string tag);
    // private: _allTags, RefreshSuggestions(), RefreshCreatorArt(), DoAddTag()
}
```

**`SeriesViewModel`** (`src/VideoShelf.App/ViewModels/SeriesViewModel.cs`):
```csharp
public sealed partial class SeriesViewModel(
    SeriesSummary summary, LibraryRepository library, WatchRepository watch, IThumbnailService thumbnails) : ObservableObject
{
    public long SeriesId => summary.SeriesId;
    public string BaseTitle => summary.BaseTitle;
    public bool IsStandalone => summary.IsStandalone;
    public int EpisodeCount => summary.EpisodeCount;
    public ObservableCollection<EpisodeViewModel> Episodes { get; } = [];
    [ObservableProperty] private int _unwatchedCount = summary.UnwatchedCount;
    [ObservableProperty] private string? _thumbnailPath;
    public bool HasUnwatched => UnwatchedCount > 0;
    public event EventHandler? UnwatchedChanged;
    public event EventHandler<EpisodeView>? PlayRequested;
    public event EventHandler<SeriesViewModel>? RenameRequested;
    [RelayCommand] private void RequestRename() => RenameRequested?.Invoke(this, this);
    public void Refresh();
    public async Task LoadEpisodesAsync(CancellationToken ct);   // populates Episodes (EpisodeViewModel), wires Play + WatchedChanged→Refresh
    public async Task LoadThumbnailAsync(CancellationToken ct);  // sets ThumbnailPath from summary.ThumbnailSeedPath via thumbnails
}
```

**`EpisodeViewModel`** (`src/VideoShelf.App/ViewModels/EpisodeViewModel.cs`): `Title`, `EpisodeNo`, `IsMissing`, `[ObservableProperty] bool Watched`, `[RelayCommand] ToggleWatched`, `[RelayCommand] Play` (raises `PlayRequested(EpisodeView)`).

**Core models** (`src/VideoShelf.Core/Models/BrowseModels.cs`, namespace `VideoShelf.Core.Models`):
```csharp
public sealed record SectionSummary(long SectionId, long SourceId, string DisplayName, int SeriesCount, int UnwatchedCount, int VideoCount, string? ThumbnailSeedPath);
public sealed record SeriesSummary(long SeriesId, long SectionId, string BaseTitle, bool IsStandalone, int EpisodeCount, int UnwatchedCount, string? ThumbnailSeedPath);
public sealed record EpisodeView(long VideoId, long SeriesId, string FilePath, int EpisodeNo, string Title, bool Watched, bool Missing);
```

**`CreatorArtRepository`**: `string? GetArtPath(long)`, `void SetArtPath(long, string)`, `void ClearArtPath(long)`.
**`IThumbnailService`**: `Task<string?> GetThumbnailPathAsync(string videoPath, CancellationToken ct)`.
**`IImagePicker`**: `string? PickImage(string? initialFolder = null)`.
**`CreatorCardViewModel.LoadImageAsync`** precedence (mirror for the hero background): override path → `thumbnails.GetThumbnailPathAsync(summary.ThumbnailSeedPath)` → null.

**`MainViewModel`**: `OpenSectionAsync(long)` = `await SectionDetail.LoadAsync(id); CurrentView = AppView.SectionDetail;`. `OpenRenameToolAsync(SeriesViewModel)` wired from `SectionDetail.RenameRequested`. `enum AppView { Home, Browse, SectionDetail, RenameTool, Search }`.
**`MainWindow.xaml`** SectionDetail host: `<views:SectionDetailView DataContext="{Binding SectionDetail}" Visibility="{Binding DataContext.CurrentView, RelativeSource={RelativeSource AncestorType=Window}, Converter={StaticResource EnumToVis}, ConverterParameter=SectionDetail}" />`.
**DI**: `services.AddSingleton<SectionDetailViewModel>();` (all deps already singletons).
**Tests**: `tests/VideoShelf.App.Tests/SectionDetailViewModelTests.cs` (fixture `NewFx()` with `AppTempDb`, `NullThumbs`, `FakeImagePicker`); `tests/VideoShelf.App.Tests/TestSupport/MainViewModelTestFactory.cs`.

> **STOP-and-report check for Task 1:** confirm `library.GetSection(long)` returns a `SectionSummary`
> (with `VideoCount` + `ThumbnailSeedPath`). If it returns a leaner type lacking those, resolve the
> summary instead via `library.GetSectionSummaries().First(s => s.SectionId == sectionId)` and report
> which you used. Also READ the **actual** current `LoadAsync` body — confirm whether it already loads
> each series' thumbnails/episodes; preserve that behavior while adding the changes below.

---

## Task 1 — VM: hero data + faded-background resolution (`SectionDetailViewModel`)

**File:** `src/VideoShelf.App/ViewModels/SectionDetailViewModel.cs`. Add hero fields, resolve the
background art (override → section seed), eager-load series tile thumbnails, and make the art
set/clear commands re-resolve the background.

1. Add fields/properties near `_creatorArtPath`:

```csharp
[ObservableProperty] private string? _backgroundImagePath;
[ObservableProperty] private int _videoCount;
private string? _seedPath;   // section representative seed frame, for the background fallback
```

2. In `LoadAsync`, capture `VideoCount` + the seed, eager-load each series' thumbnail, and resolve
   the background at the end. Apply these edits to the existing method (preserve the tag/suggestion/
   art logic already there):

```csharp
public async Task LoadAsync(long sectionId)
{
    SectionId = sectionId;

    var section = library.GetSection(sectionId);   // SectionSummary (see STOP note)
    DisplayName = section?.DisplayName ?? "";
    VideoCount = section?.VideoCount ?? 0;
    _seedPath = section?.ThumbnailSeedPath;

    var (summaries, sectionTags, allTags) = await Task.Run(() => (
        library.GetSeriesSummaries(sectionId),
        tags.GetTags(sectionId),
        tags.GetAllTags()));
    _allTags = allTags;

    SeriesList.Clear();
    foreach (var s in summaries)
    {
        var svm = new SeriesViewModel(s, library, watch, thumbnails);
        svm.PlayRequested += (_, e) => PlayRequested?.Invoke(this, e);
        svm.RenameRequested += (_, sv) => RenameRequested?.Invoke(this, sv);
        SeriesList.Add(svm);
        _ = svm.LoadThumbnailAsync(CancellationToken.None);   // eager tile art (cached + fail-safe)
    }

    Tags.Clear();
    foreach (var t in sectionTags) Tags.Add(t);
    RefreshSuggestions();
    RefreshCreatorArt();                 // existing: sets CreatorArtPath from the override
    await ResolveBackgroundAsync();
}

private async Task ResolveBackgroundAsync()
{
    if (!string.IsNullOrWhiteSpace(CreatorArtPath)) { BackgroundImagePath = CreatorArtPath; return; }
    if (string.IsNullOrWhiteSpace(_seedPath)) { BackgroundImagePath = null; return; }
    BackgroundImagePath = await thumbnails.GetThumbnailPathAsync(_seedPath!, CancellationToken.None);
}
```

> If the real `LoadAsync` already calls `svm.LoadThumbnailAsync`/`LoadEpisodesAsync`, keep its existing
> calls and only ADD the `section`/`VideoCount`/`_seedPath` capture + `await ResolveBackgroundAsync()`.
> Do not double-load. STOP-and-report if the structure differs materially.

3. Make the art commands async so they re-resolve the background (the XAML binds
   `SetCreatorArtCommand`/`ClearCreatorArtCommand`, which still resolve as the generated
   `AsyncRelayCommand`):

```csharp
[RelayCommand]
private async Task SetCreatorArt()
{
    if (SectionId <= 0) return;
    var picked = imagePicker.PickImage();
    if (string.IsNullOrWhiteSpace(picked)) return;
    art.SetArtPath(SectionId, picked);
    CreatorArtPath = picked;
    await ResolveBackgroundAsync();
}

[RelayCommand]
private async Task ClearCreatorArt()
{
    if (SectionId <= 0) return;
    art.ClearArtPath(SectionId);
    CreatorArtPath = null;
    await ResolveBackgroundAsync();
}
```

**Tests** — `tests/VideoShelf.App.Tests/SectionDetailViewModelTests.cs`. Existing tests that invoke
`SetCreatorArtCommand`/`ClearCreatorArtCommand` synchronously must switch to
`await vm.SetCreatorArtCommand.ExecuteAsync(null)` (now `AsyncRelayCommand`). Add:
- After `LoadAsync`, `VideoCount` equals the section's video count and `BackgroundImagePath` resolves
  (with `NullThumbs` returning null + no override → `BackgroundImagePath` is null; with a `FakeImagePicker`
  returning a path then `SetCreatorArt`, `BackgroundImagePath` equals that override path).
- `SetCreatorArt` with a picker returning a path sets `CreatorArtPath` **and** `BackgroundImagePath`;
  `ClearCreatorArt` nulls `CreatorArtPath` and re-resolves the background (to seed/null).

> STOP-and-report if `GetSection` doesn't expose `VideoCount`/`ThumbnailSeedPath` (use the
> `GetSectionSummaries().First(...)` fallback and note it), or if existing tests can't be made to build.

---

## Task 2 — VM: series accordion (`SeriesViewModel`)

**File:** `src/VideoShelf.App/ViewModels/SeriesViewModel.cs`. Add expand state + an `Activate`
command (standalone → play; multi-episode → toggle expand with lazy episode load), reusing the
existing `LoadEpisodesAsync`.

```csharp
[ObservableProperty] private bool _isExpanded;
private bool _episodesLoaded;

public string EpisodeCountLabel => IsStandalone ? "Standalone" : $"{EpisodeCount} episodes";

[RelayCommand]
private async Task Activate()
{
    if (IsStandalone)
    {
        await EnsureEpisodesLoadedAsync();
        Episodes.FirstOrDefault()?.PlayCommand.Execute(null);   // raises PlayRequested via the episode
        return;
    }
    IsExpanded = !IsExpanded;
    if (IsExpanded) await EnsureEpisodesLoadedAsync();
}

private async Task EnsureEpisodesLoadedAsync()
{
    if (_episodesLoaded) return;
    await LoadEpisodesAsync(CancellationToken.None);
    _episodesLoaded = true;
}
```

Add `using System.Linq;` if `FirstOrDefault` isn't already available. Keep `RequestRename`,
`LoadEpisodesAsync`, `LoadThumbnailAsync`, `Refresh` unchanged.

**Tests** — new file `tests/VideoShelf.App.Tests/SeriesViewModelTests.cs` (mirror the App.Tests arrange
idiom: `AppTempDb` + `LibraryRepository`/`WatchRepository` + `NullThumbs`; seed a multi-episode series
and a standalone via `UpsertSeries`/`UpsertVideo`, build `SeriesSummary` via `library.GetSeriesSummaries`).
Cover:
- `Activate` on a **multi-episode** series sets `IsExpanded=true` and loads `Episodes` (count > 0); a
  second `Activate` collapses (`IsExpanded=false`) without reloading.
- `Activate` on a **standalone** series raises `PlayRequested` (subscribe and assert the event fired with
  the single episode) and does **not** set `IsExpanded`.
- `EpisodeCountLabel` is `"Standalone"` for a standalone and `"N episodes"` otherwise.

---

## Task 3 — View: redesign `SectionDetailView.xaml` (hero + accordion grid)

**File:** `src/VideoShelf.App/Views/SectionDetailView.xaml`. READ the current file first to copy its
exact `<UserControl …>` opening tag, `xmlns` set (needs `ui` + `views`), and any resources header
verbatim. Then replace the body with the structure below (a `ScrollViewer` → hero banner → tag-input row
→ series accordion grid). Keep all existing bindings (`DisplayName`, `Tags`, `RemoveTagCommand`,
`TagInput`, `AddTagCommand`, `Suggestions`, `AddSuggestionCommand`, `SetCreatorArtCommand`,
`ClearCreatorArtCommand`, `HasCreatorArt`, `SeriesList`).

```xml
<ScrollViewer VerticalScrollBarVisibility="Auto">
    <StackPanel>

        <!-- HERO BANNER: creator art (dimmed) + name + count + tag pills + art actions -->
        <Grid Height="240" ClipToBounds="True">
            <Image Source="{Binding BackgroundImagePath, IsAsync=True}" Stretch="UniformToFill"/>
            <!-- additive dim gradient for readability (not a control re-template) -->
            <Border>
                <Border.Background>
                    <LinearGradientBrush StartPoint="0,0" EndPoint="0,1">
                        <GradientStop Color="#22000000" Offset="0"/>
                        <GradientStop Color="#E6000000" Offset="1"/>
                    </LinearGradientBrush>
                </Border.Background>
            </Border>
            <StackPanel VerticalAlignment="Bottom" Margin="24,0,24,16">
                <TextBlock Text="{Binding DisplayName}" FontSize="28" FontWeight="Bold" Foreground="White"/>
                <TextBlock Foreground="White" Opacity="0.85" Margin="0,2,0,10">
                    <Run Text="{Binding VideoCount, Mode=OneWay}"/><Run Text=" videos"/>
                </TextBlock>
                <StackPanel Orientation="Horizontal">
                    <ItemsControl ItemsSource="{Binding Tags}" VerticalAlignment="Center">
                        <ItemsControl.ItemsPanel><ItemsPanelTemplate><WrapPanel/></ItemsPanelTemplate></ItemsControl.ItemsPanel>
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Border Background="#55FFFFFF" CornerRadius="{StaticResource ControlRadius}" Padding="8,4" Margin="0,0,6,0">
                                    <StackPanel Orientation="Horizontal">
                                        <TextBlock Text="{Binding}" Foreground="White" VerticalAlignment="Center" Margin="0,0,4,0"/>
                                        <Button Content="✕" Padding="2,0" Background="Transparent" BorderThickness="0" Foreground="White"
                                                Command="{Binding DataContext.RemoveTagCommand, RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                                                CommandParameter="{Binding}"/>
                                    </StackPanel>
                                </Border>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                    <ui:Button Content="Set image…" Command="{Binding SetCreatorArtCommand}" Margin="8,0,0,0"/>
                    <ui:Button Content="Use default" Command="{Binding ClearCreatorArtCommand}" Margin="8,0,0,0"
                               Visibility="{Binding HasCreatorArt, Converter={StaticResource BoolToVisibility}}"/>
                </StackPanel>
            </StackPanel>
        </Grid>

        <!-- TAG INPUT + suggestions -->
        <StackPanel Orientation="Horizontal" Margin="24,12,24,0">
            <ui:TextBox Text="{Binding TagInput, UpdateSourceTrigger=PropertyChanged}" PlaceholderText="Add tag…" Width="220"/>
            <ui:Button Content="Add tag" Command="{Binding AddTagCommand}" Margin="8,0,0,0"/>
        </StackPanel>
        <ItemsControl ItemsSource="{Binding Suggestions}" Margin="24,8,24,0">
            <ItemsControl.ItemsPanel><ItemsPanelTemplate><WrapPanel/></ItemsPanelTemplate></ItemsControl.ItemsPanel>
            <ItemsControl.ItemTemplate>
                <DataTemplate>
                    <Button Content="{Binding}" Margin="0,0,6,6"
                            Command="{Binding DataContext.AddSuggestionCommand, RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                            CommandParameter="{Binding}"/>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>

        <!-- SERIES GRID (accordion tiles) -->
        <TextBlock Text="SERIES" Style="{StaticResource SectionHeader}" Margin="24,16,24,8"/>
        <ItemsControl ItemsSource="{Binding SeriesList}" Margin="24,0,24,24">
            <ItemsControl.ItemsPanel><ItemsPanelTemplate><WrapPanel/></ItemsPanelTemplate></ItemsControl.ItemsPanel>
            <ItemsControl.ItemTemplate>
                <DataTemplate>
                    <Border Width="240" Margin="0,0,12,12" Background="{StaticResource SubtleFillBrush}"
                            CornerRadius="{StaticResource CardRadius}" VerticalAlignment="Top">
                        <Border.ContextMenu>
                            <ContextMenu>
                                <MenuItem Header="Rename files…" Command="{Binding RequestRenameCommand}"/>
                            </ContextMenu>
                        </Border.ContextMenu>
                        <StackPanel>
                            <!-- clickable tile header = Activate (play standalone / toggle expand) -->
                            <Button Command="{Binding ActivateCommand}" Background="Transparent" BorderThickness="0"
                                    Padding="0" Cursor="Hand" HorizontalContentAlignment="Stretch">
                                <StackPanel>
                                    <Border Height="135" Background="{StaticResource ThumbPlaceholderBrush}"
                                            CornerRadius="{StaticResource CardImageRadius}" ClipToBounds="True">
                                        <Image Source="{Binding ThumbnailPath, IsAsync=True}" Stretch="UniformToFill"/>
                                    </Border>
                                    <DockPanel Margin="8,6,8,6">
                                        <Border DockPanel.Dock="Right" Background="{StaticResource AccentBrush}"
                                                CornerRadius="{StaticResource ControlRadius}" Padding="6,1" VerticalAlignment="Center"
                                                Visibility="{Binding HasUnwatched, Converter={StaticResource BoolToVisibility}}">
                                            <TextBlock FontSize="11" Foreground="#101010">
                                                <Run Text="{Binding UnwatchedCount, Mode=OneWay}"/><Run Text=" new"/>
                                            </TextBlock>
                                        </Border>
                                        <StackPanel>
                                            <TextBlock Text="{Binding BaseTitle}" FontWeight="SemiBold" TextTrimming="CharacterEllipsis"/>
                                            <TextBlock Text="{Binding EpisodeCountLabel}" Opacity="0.7" FontSize="12"/>
                                        </StackPanel>
                                    </DockPanel>
                                </StackPanel>
                            </Button>
                            <!-- expanded episode list (accordion) -->
                            <StackPanel Margin="8,0,8,8"
                                        Visibility="{Binding IsExpanded, Converter={StaticResource BoolToVisibility}}">
                                <ItemsControl ItemsSource="{Binding Episodes}">
                                    <ItemsControl.ItemTemplate>
                                        <DataTemplate>
                                            <DockPanel Margin="0,2"
                                                       Opacity="{Binding IsMissing, Converter={StaticResource MissingToOpacity}}">
                                                <CheckBox DockPanel.Dock="Right" IsChecked="{Binding Watched, Mode=OneWay}"
                                                          Command="{Binding ToggleWatchedCommand}"/>
                                                <ui:Button DockPanel.Dock="Left" Content="&#9654;" Padding="6,2" Margin="0,0,6,0"
                                                           ToolTip="Play" Command="{Binding PlayCommand}"/>
                                                <TextBlock Text="{Binding Title}" VerticalAlignment="Center"
                                                           TextTrimming="CharacterEllipsis"/>
                                            </DockPanel>
                                        </DataTemplate>
                                    </ItemsControl.ItemTemplate>
                                </ItemsControl>
                                <ui:Button Content="Rename files…" Command="{Binding RequestRenameCommand}"
                                           Margin="0,6,0,0" HorizontalAlignment="Left"/>
                            </StackPanel>
                        </StackPanel>
                    </Border>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>

    </StackPanel>
</ScrollViewer>
```

> **STOP-and-report if:** any `{StaticResource …}` key used here (`ControlRadius`, `CardRadius`,
> `CardImageRadius`, `ThumbPlaceholderBrush`, `SubtleFillBrush`, `AccentBrush`, `SectionHeader`,
> `BoolToVisibility`, `MissingToOpacity`) does **not** exist in the project's resources/`DesignTokens.xaml`.
> Use the key the existing `SectionDetailView.xaml`/`MainWindow.xaml` already use for the same purpose
> (the digest shows the old file used `CardRadius`, `ControlRadius`, `ThumbPlaceholderBrush`,
> `SubtleFillBrush`, `AccentBrush`, `SectionHeader`, `ThumbnailImage`, `BoolToVisibility`,
> `MissingToOpacity`) and report any substitution. Keep the `<UserControl>` opening tag + resources
> header exactly as the current file has them.

---

## Task 4 — Harness: capture the expanded accordion in the SectionDetail sweep

**File:** `src/VideoShelf.App/Harness/HarnessRunner.cs`. The `SectionDetail` view opens the richest
series' section. After opening, **expand the first multi-episode series** so the screenshot shows the
accordion open (and exercises lazy episode loading). Edit the `"SectionDetail"` case in `NavigateAsync`:

```csharp
case "SectionDetail":
    await _main.OpenSectionAsync((await FindRichestSeriesAsync()).SectionId);
    var expandable = _main.SectionDetail.SeriesList.FirstOrDefault(s => !s.IsStandalone);
    if (expandable is not null) await expandable.ActivateCommand.ExecuteAsync(null);
    break;
```

Add `using System.Linq;` to the file if needed. `ActivateCommand` is the generated `AsyncRelayCommand`
from Task 2 → `ExecuteAsync(null)`.

> STOP-and-report if `HarnessRunner`'s structure differs materially from "a `NavigateAsync` switch that
> drives `MainViewModel` then `SettleAsync` writes the done-signal" (implement the VM parts regardless).

---

## Task 5 — Build + full test gate

From the worktree root:

```powershell
dotnet build VideoShelf.slnx -c Release -v minimal
dotnet test  VideoShelf.slnx -c Release --nologo -v q
```

**Expected:** build clean; **all tests pass, 0 failures**, count higher than the 244 baseline (App gains
from Tasks 1 & 2; Core unchanged at 118). Fix any failure's root cause — don't weaken assertions; apply
`systematic-debugging` if non-obvious. Note: `BrowseFanoutTests.SelectingSection_loads_series_with_episodes_and_thumbnails`
is a known pre-existing parallel-execution flake (passes in isolation/on re-run) — if it fails in a full
run, re-run to confirm it's the flake, not an M9 regression.

---

## Task 6 — Visual sweep (creator page)

Run the sweep and have a **Sonnet subagent view the PNGs and return a TEXT verdict** (never load PNGs
into the controller). Regenerate fixtures fresh first (stale-prone cache):

```powershell
Remove-Item -Recurse -Force "$env:TEMP\vs-fixtures" -ErrorAction SilentlyContinue
& "<worktree>\tools\harness\Generate-Fixtures.ps1" -OutDir "$env:TEMP\vs-fixtures"
& "<worktree>\tools\harness\Run-VisualSweep.ps1"
```

Judge `section-detail.png` (the creator page). Acceptance criteria (judge the main content area; ignore
the **known pre-existing player bar** at the very bottom — deferred to M10):
- A **hero banner** at the top: creator art filling the banner, dimmed, with the **creator name** (large)
  + **"N videos"** + applied **tag pills** + **"Set image…"** button overlaid, fading into the content below.
- A **TAGS** input row + suggestions below the hero.
- A **SERIES** grid of tiles (thumbnail + title + "N episodes"/"Standalone" + unwatched badge).
- The **first multi-episode series is expanded** (accordion open) showing episode rows (▶ play + watched
  checkbox) and a visible **"Rename files…"** button.
- No stacked/overlapping hosts; tiles not clipped.

If a real defect surfaces, fix it (additive-only) and re-sweep. Harness gotchas (from M6): unlocked
composited desktop required (else black); `Generate-Fixtures.ps1 -Force` if fixtures look stale; the
sweep already handles the Mica settle + TOPMOST toggle.

---

## Task 7 — Ship

1. Commit each task on the feature branch (author `yovanmc` + the Opus co-author trailer; no Codex trailer).
2. **Flip the ROADMAP.md M9 row** to `✅ Merged` (PR #) with a one-line summary, and append an **M9
   shipped** decision-log entry capturing: the hero-banner + inline-accordion decisions, the
   `BackgroundImagePath` resolution (override→seed, re-resolved on set/clear), `SeriesViewModel.Activate`,
   the rename fold-in interpretation (per-series context-menu + expanded button; no creator-level rename),
   the final test count, and any STOP-and-report deviations. This flip rides on the M9 branch.
3. Push; open a PR; `& "C:\Program Files\GitHub CLI\gh.exe" pr checks <PR#> --watch` (sleep ~20s first);
   merge `--merge --delete-branch` from the **main repo root**; sync `main`; remove the worktree.
4. Run `requesting-code-review` on the whole branch before merge; address findings with
   `receiving-code-review` rigor.
5. Ping the user the Phase-B→next-plan handoff (M10 — Immersive player redesign, which also folds in the
   deferred player-bar bug, PiP black-frame, and seek-preview decoder).

## Acceptance checklist

- [ ] Task 1 — `SectionDetailViewModel`: `VideoCount` + `BackgroundImagePath` (override→seed, re-resolved on set/clear) + eager tile thumbnails + tests
- [ ] Task 2 — `SeriesViewModel`: `IsExpanded` + `Activate` (standalone-play / multi-episode lazy-expand) + `EpisodeCountLabel` + tests
- [ ] Task 3 — `SectionDetailView.xaml`: hero banner + tag editor + accordion series grid + rename (context-menu + expanded button)
- [ ] Task 4 — harness expands the first multi-episode series for the sweep
- [ ] Task 5 — build clean + full gate green, count > 244
- [ ] Task 6 — sweep PASS (hero + tags + series grid + expanded accordion + rename) via subagent text verdict
- [ ] Task 7 — ROADMAP flipped, PR merged, CI green, handoff pinged
