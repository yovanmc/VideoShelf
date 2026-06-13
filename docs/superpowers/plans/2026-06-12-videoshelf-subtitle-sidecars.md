# M13 — Subtitle Sidecars (Plan)

> **Written for Sonnet execution.** Each task is bite-sized: implement → write/extend the failing
> test → make it pass → run the gate → commit. **If anything here does not match the real code (a
> signature, member, or libVLC API), STOP and report rather than guess.** This is the (a) half of the
> original M13; the play-queue (b) half is now its own milestone (M14).

## Goal (from the ROADMAP M13 row)
**External subtitle sidecars:** auto-detect a `.srt`/`.ass` (and friends) sitting next to the video file
and load it, PLUS a manual **"Add subtitle file…"** button in the player — both fed into the EXISTING
subtitle-track picker via libVLC `Media.AddSlave` / `MediaPlayer.AddSlave`. **NO downloading / online.**

## Pre-locked findings from the code digest (do NOT re-investigate)
- **The subtitle-track picker already exists end-to-end:** `IPlaybackEngine.GetSubtitleTracks()/SetSubtitleTrack(int)`
  (built on libVLC `SpuDescription`/`SetSpu`/`Spu`), `PlayerViewModel.SubtitleTracks`
  (`ObservableCollection<TrackOption>`), `SelectedSubtitleTrack` (`OnSelectedSubtitleTrackChanged` →
  `engine.SetSubtitleTrack`), and the `PlayerView.xaml` subtitle `ComboBox`. `TrackOption(int Id, string Label)`
  with `TrackOption.SubtitlesOffId = -1`. `HasSubtitleTracks => SubtitleTracks.Count > 1` (the always-present
  "Off" entry is #1; a loaded sidecar makes it 2 → the ComboBox becomes visible). `RefreshTracks()` repopulates
  the OCs from the engine and is called from `PlayerView.OnLoaded`.
- **`LibVlcPlaybackEngine.Load(string filePath)`** does `var media = new Media(_libVlc, new Uri(filePath));
  _player.Media = media; media.Dispose();` (the player keeps its own ref). **`AddSlave` is used NOWHERE today.**
- **Two libVLC slave APIs (pinned LibVLCSharp 3.9.7.1):**
  - `Media.AddSlave(MediaSlaveType type, uint priority, string uri)` — must be called on the `Media`
    object BEFORE it's disposed/played → use for **auto-detected sidecars at Load time**, before `media.Dispose()`.
  - `MediaPlayer.AddSlave(MediaSlaveType type, string uri, bool select)` — works WHILE playing → use for
    the **manual "Add subtitle file…"** on an already-loaded video. `MediaSlaveType.Subtitle` exists.
  - `uri` must be a real URI — use `new Uri(path).AbsoluteUri` (yields `file:///…`).
  - **STOP-and-report if either `AddSlave` overload / `MediaSlaveType.Subtitle` differs in the pinned API.**
- **File-picker pattern exists:** `IImagePicker`/`ImagePicker` + `IFolderPicker`/`FolderPicker`, each a thin
  `OpenFileDialog`/`OpenFolderDialog` wrapper, registered `AddSingleton`, with test fakes
  `FakeImagePicker`/`FakeFolderPicker` in `tests/VideoShelf.App.Tests/TestSupport/`. **There is NO `IFilePicker`
  for arbitrary files yet** — M13 adds `ISubtitleFilePicker`.
- **`PlayerViewModel` ctor (5 params):** `(IPlaybackEngine engine, LibraryRepository library,
  WatchRepository watch, SettingsRepository settings, ResumePolicy resumePolicy)`. The current video is a
  private `EpisodeView? _current` (set in `Open`); `EpisodeView.FilePath` is public on the record but the VM
  does NOT expose it yet. PlayerViewModel is built by a **manual DI factory lambda** (object-init sets
  `CaptureDirectory`/`SeekPreviewDirectory`), and constructed directly in `PlayerEndOfMediaTests` /
  `MainViewModelPlaybackTests` — adding a ctor param fans out to those sites.
- **`FakePlaybackEngine`** (`tests/.../TestSupport/FakePlaybackEngine.cs`) implements `IPlaybackEngine` with
  `List<TrackOption> SubtitleTracks` and `Raise…` drivers — extend it for the new method.
- **`MainViewModel` ctor is 11 params** now; this milestone does NOT change it (subtitle work lives in
  PlayerViewModel/engine). `IFileSystem` exists (M5 rename tool) but is NOT needed here.

## Design decisions (made; don't re-decide)
1. **Sidecar discovery = a pure Core helper** (`SubtitleSidecars.Find`) that takes the video path + its
   sibling file list and returns matching sidecar paths — unit-tested with plain string lists, no
   filesystem. The engine supplies `Directory.GetFiles(dir)` and calls `Media.AddSlave` for each at Load.
2. **Two attach paths:** auto (in `Load`, via `Media.AddSlave` before `Dispose`) and manual (a new
   `IPlaybackEngine.AddSubtitle(path)` → `MediaPlayer.AddSlave(..., select:true)` + `RefreshTracks()`).
3. **Manual picker** = a new `ISubtitleFilePicker` (mirrors `IImagePicker`), filtered to subtitle extensions,
   opening in the current video's folder. A new `[RelayCommand] AddSubtitleFile` on `PlayerViewModel`.
4. **Matching rule:** a sibling is a sidecar if its name (case-insensitive) ends with a subtitle extension
   AND its name-without-extension equals the video's base name OR starts with `"<base>."` (language-tagged,
   e.g. `movie.en.srt`). Extensions: `.srt .ass .ssa .vtt .sub`.

## Conventions (from the runbook)
- Worktree under `.worktrees/`; branch `feat/subtitle-sidecars`. Gate: `dotnet test VideoShelf.slnx -c
  Release --nologo -v q`. Build quiet: `-v minimal`. `gh` at `& "C:\Program Files\GitHub CLI\gh.exe"`.
  Merge `--merge` from the main repo root. Commit author `yovanmc` + trailer
  `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>` (BOM-free, no Codex trailer). One commit/task.
- **Theming rule (binds):** additive only — no `Style`/`ControlTemplate` override of a WPF-UI control.
- **libVLC testability:** keep `LibVlcPlaybackEngine` changes THIN/integration (uncovered by unit tests);
  put logic in the pure `SubtitleSidecars` helper + the VM command (tested via `FakePlaybackEngine` +
  `FakeSubtitleFilePicker`). Off-thread care unchanged (no new event-thread callbacks here).
- Known **single-test parallel flake**: if exactly one *unrelated* test fails, re-run that project in
  isolation to confirm before reporting.

---

## Task 1 — Core: pure subtitle-sidecar discovery helper

**File:** `src/VideoShelf.Core/Playback/SubtitleSidecars.cs` (new; create the `Playback` folder if absent —
match the namespace convention `VideoShelf.Core.Playback` or wherever pure helpers live; if Core has no
such folder, use `VideoShelf.Core` root and report).
```csharp
namespace VideoShelf.Core.Playback;

public static class SubtitleSidecars
{
    public static readonly string[] Extensions = { ".srt", ".ass", ".ssa", ".vtt", ".sub" };

    /// <summary>Given a video path and the list of files sitting in its folder, returns the sibling
    /// paths that are subtitle sidecars for it: a file whose extension is a subtitle extension AND whose
    /// name-without-extension equals the video's base name OR starts with "&lt;base&gt;." (language-tagged,
    /// e.g. movie.en.srt). Case-insensitive. Never returns the video itself.</summary>
    public static IReadOnlyList<string> Find(string videoPath, IEnumerable<string> siblingFilePaths)
    {
        var baseName = System.IO.Path.GetFileNameWithoutExtension(videoPath);
        var result = new List<string>();
        foreach (var sib in siblingFilePaths)
        {
            if (string.Equals(sib, videoPath, StringComparison.OrdinalIgnoreCase)) continue;
            var ext = System.IO.Path.GetExtension(sib);
            if (!Extensions.Any(e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase))) continue;
            var sibName = System.IO.Path.GetFileNameWithoutExtension(sib); // e.g. "movie" or "movie.en"
            if (string.Equals(sibName, baseName, StringComparison.OrdinalIgnoreCase)
                || sibName.StartsWith(baseName + ".", StringComparison.OrdinalIgnoreCase))
                result.Add(sib);
        }
        return result;
    }
}
```
**Tests** (`tests/VideoShelf.Core.Tests/…`): `SubtitleSidecarsTests`:
1. exact-base match: `["C:\m\movie.srt","C:\m\movie.mkv"]` for `movie.mkv` → returns `movie.srt`.
2. language-tagged: includes `movie.en.srt` and `movie.fr.ass`.
3. excludes non-subtitle siblings (`movie.jpg`, `other.srt` where base differs) and the video itself.
4. case-insensitive extension (`MOVIE.SRT`).

**Verify** gate green. **Commit** `M13: SubtitleSidecars discovery helper`.

---

## Task 2 — Engine: attach sidecars at Load + add a manual AddSubtitle method

**Files:** `src/VideoShelf.App/Services/IPlaybackEngine.cs`, `LibVlcPlaybackEngine.cs`,
`tests/VideoShelf.App.Tests/TestSupport/FakePlaybackEngine.cs`.

### 2a. Interface — add one method (next to `SetSubtitleTrack`)
```csharp
/// <summary>Attaches an external subtitle file to the currently-loaded media and selects it.</summary>
void AddSubtitle(string subtitlePath);
```

### 2b. `LibVlcPlaybackEngine` — implement (READ the file; match its try/catch + `Media`/`MediaPlayer` usage)
- In `Load(string filePath)`, BEFORE `media.Dispose()`, attach auto-detected sidecars:
```csharp
var media = new Media(_libVlc, new Uri(filePath));
try
{
    var dir = System.IO.Path.GetDirectoryName(filePath);
    if (!string.IsNullOrEmpty(dir) && System.IO.Directory.Exists(dir))
    {
        foreach (var sub in VideoShelf.Core.Playback.SubtitleSidecars.Find(filePath, System.IO.Directory.GetFiles(dir)))
            media.AddSlave(MediaSlaveType.Subtitle, 4, new Uri(sub).AbsoluteUri);
    }
}
catch { /* sidecar attach is best-effort; never block playback */ }
_player.Media = media;
media.Dispose();
```
- Add the method:
```csharp
public void AddSubtitle(string subtitlePath)
{
    try { _player.AddSlave(MediaSlaveType.Subtitle, new Uri(subtitlePath).AbsoluteUri, true); }
    catch { }
}
```
> **STOP-and-report** if `Media.AddSlave(MediaSlaveType, uint, string)` or
> `MediaPlayer.AddSlave(MediaSlaveType, string, bool)` or `MediaSlaveType.Subtitle` don't exist with these
> shapes in the pinned LibVLCSharp 3.9.7.1 (check the package). Do not guess the slave API.

### 2c. `FakePlaybackEngine` — implement `AddSubtitle` so tests can verify
```csharp
public List<string> AddedSubtitles { get; } = new();
public void AddSubtitle(string subtitlePath)
{
    AddedSubtitles.Add(subtitlePath);
    // simulate the new track surfacing in the picker:
    SubtitleTracks.Add(new TrackOption(SubtitleTracks.Count, System.IO.Path.GetFileName(subtitlePath)));
}
```
(Match the fake's existing field/style. If `SubtitleTracks` is pre-seeded with "Off", keep that.)

**Verify** builds + gate green (no new behavior test here beyond the fake compiling; the command test is Task 4).
**Commit** `M13: engine attaches subtitle sidecars on load + manual AddSubtitle`.

---

## Task 3 — App: `ISubtitleFilePicker` + concrete + DI + fake

**Files:** `src/VideoShelf.App/Services/ISubtitleFilePicker.cs` (new), `SubtitleFilePicker.cs` (new),
`ServiceCollectionExtensions.cs`, `tests/VideoShelf.App.Tests/TestSupport/FakeSubtitleFilePicker.cs` (new).

READ `IImagePicker.cs`/`ImagePicker.cs` and mirror them exactly.
```csharp
public interface ISubtitleFilePicker
{
    /// <summary>Opens a file dialog filtered to subtitle files; returns the chosen path or null.</summary>
    string? PickSubtitle(string? initialFolder = null);
}
```
`SubtitleFilePicker : ISubtitleFilePicker` — `OpenFileDialog` with
`Filter = "Subtitles|*.srt;*.ass;*.ssa;*.vtt;*.sub|All files|*.*"`, `InitialDirectory = initialFolder` when set,
return `dlg.ShowDialog() == true ? dlg.FileName : null`. Match `ImagePicker`'s structure.
DI: `services.AddSingleton<ISubtitleFilePicker, SubtitleFilePicker>();` (next to `IImagePicker`).
`FakeSubtitleFilePicker : ISubtitleFilePicker` — a settable `public string? NextResult { get; set; }` returned
by `PickSubtitle` (mirror `FakeImagePicker`).

**Verify** builds + gate green. **Commit** `M13: ISubtitleFilePicker (+ fake, DI)`.

---

## Task 4 — PlayerViewModel: AddSubtitleFile command + current-path

**Files:** `src/VideoShelf.App/ViewModels/PlayerViewModel.cs`, the DI factory lambda in
`ServiceCollectionExtensions.cs`, and the test construction sites (`PlayerEndOfMediaTests`,
`MainViewModelPlaybackTests`, plus `MainViewModelTestFactory` if it builds PlayerViewModel).

- Add ctor param `ISubtitleFilePicker subtitlePicker` (PlayerViewModel goes 5→6 params). READ the ctor +
  every construction site and update them (the DI factory lambda keeps its object-init for
  `CaptureDirectory`/`SeekPreviewDirectory`).
- Expose the current path + a guard:
```csharp
public string? CurrentFilePath => _current?.FilePath;
public bool CanAddSubtitle => _current is not null;
```
  Raise `OnPropertyChanged(nameof(CurrentFilePath))` and `nameof(CanAddSubtitle)` wherever `_current` is set
  (in `Open` and on close/clear).
- Add the command:
```csharp
[RelayCommand]
private void AddSubtitleFile()
{
    if (_current is not { } cur) return;
    var folder = System.IO.Path.GetDirectoryName(cur.FilePath);
    var path = subtitlePicker.PickSubtitle(folder);
    if (string.IsNullOrEmpty(path)) return;
    engine.AddSubtitle(path);
    RefreshTracks();
    // auto-select the newly added subtitle (the last non-Off track), if present:
    SelectedSubtitleTrack = SubtitleTracks.LastOrDefault(t => t.Id != TrackOption.SubtitlesOffId)
                            ?? SelectedSubtitleTrack;
}
```
> Match the real field name (`_current`) and how `RefreshTracks` repopulates. If selecting the last track is
> awkward with the engine's id scheme, at minimum ensure `RefreshTracks()` runs so the new track appears;
> the user can pick it. Keep it simple.

**Tests** (`tests/VideoShelf.App.Tests/…`, mirror `PlayerEndOfMediaTests` construction with `FakePlaybackEngine`
+ a `FakeSubtitleFilePicker`): `AddSubtitleFile_attaches_and_surfaces_track` — `Open(ep)`, set
`fakePicker.NextResult = @"C:\m\movie.en.srt"`, execute `AddSubtitleFileCommand`; assert
`fakeEngine.AddedSubtitles` contains that path AND `vm.SubtitleTracks` now contains a track labeled
`movie.en.srt` (and `HasSubtitleTracks` is true). Also `AddSubtitleFile_noop_when_picker_cancels`
(`NextResult = null` → no engine call). Also assert `CurrentFilePath`/`CanAddSubtitle` reflect `Open`.

**Verify** gate green. **Commit** `M13: PlayerViewModel AddSubtitleFile command + CurrentFilePath`.

---

## Task 5 — PlayerView: "Add subtitle file…" button

**File:** `src/VideoShelf.App/Views/PlayerView.xaml`.

READ the bottom-transport secondary-controls `StackPanel` (the one collapsed in PiP, holding the subtitle
ComboBox at ~lines 117–121). Add, immediately AFTER the subtitle `ComboBox`, a button to add a sidecar:
```xml
<ui:Button Margin="6,0,0,0" Command="{Binding Player.AddSubtitleFileCommand}"
           ToolTip="Add subtitle file…" Appearance="Transparent" Content="+ Sub" />
```
> Confirm the `ui:` xmlns is present in PlayerView.xaml (it uses `ui:Button` elsewhere). If a Fluent
> `ui:SymbolIcon` for subtitles exists in the pinned WPF-UI you MAY use it instead of the `+ Sub` text — but
> the icon system is M15's job, so a short text label is fine and lower-risk. Keep the button INSIDE the
> secondary-controls group so it collapses in PiP like the other secondary controls.

**Verify** builds + gate green. **Commit** `M13: Add-subtitle-file button in the player transport`.

---

## Task 6 — Harness sweep: a sidecar fixture + verify

### 6a. Seed a sidecar next to the play clip
READ `tools/harness/Generate-Fixtures.ps1` and the sweep's `--play` clip (`$playClip = …\Sintel (2010).mp4`).
After the fixtures are generated, write a tiny sibling `.srt` next to the play clip so the auto-detect
surfaces a subtitle track in the `player` capture:
```powershell
# minimal valid SRT beside the play clip so the sidecar auto-loads in the sweep
$srt = [System.IO.Path]::ChangeExtension($playClip, '.srt')
if (-not (Test-Path $srt)) {
@"
1
00:00:00,500 --> 00:00:04,000
VideoShelf sidecar subtitle test.
"@ | Set-Content -Path $srt -Encoding UTF8
}
```
Place this in `Run-VisualSweep.ps1` right after the `Generate-Fixtures.ps1` call (or in Generate-Fixtures
itself). **If wiring this is awkward, SKIP it and note "subtitle sidecar verified by unit tests + button
presence"** — don't block the milestone on the visual.

### 6b. Run the sweep (pwsh 7) + subagent verdict
**Before running, enumerate top-level windows and disregard the known external "Webcam Streams Recorder"
top-left GDI bleed** (see ROADMAP decision log). Run `tools/harness/Run-VisualSweep.ps1` under `pwsh` 7 on an
unlocked composited desktop. Dispatch ONE Sonnet subagent to Read the PNGs in `PNG_DIR` and return PASS/FAIL,
against:
1. **`player.png`** — the transport shows the **subtitle `ComboBox`** (a sidecar loaded → `HasSubtitleTracks`
   true) AND the new **"+ Sub" / "Add subtitle file…" button** beside it. (If the sidecar wasn't seeded, at
   least the "Add subtitle" button is present.)
2. **No regressions** — player full-window immersive still correct; `pip.png` still collapses secondary
   controls (the new button is inside that collapsing group, so it must be ABSENT in PiP); Home/stats/rails
   from M11/M12 intact.

**On FAIL** fix via the implementer loop and re-sweep. **Commit** any harness changes
`M13: harness seeds a subtitle sidecar for the player sweep`.

---

## Finish (controller)
1. Final gate `dotnet test VideoShelf.slnx -c Release --nologo -v q` — 0 failures (expect ~280+ tests:
   276 baseline + SubtitleSidecars + AddSubtitleFile tests).
2. Final whole-branch review (fresh Sonnet) over `git diff main..HEAD`: theming-rule compliance (the button
   is a plain `ui:Button`, no re-template), the sidecar attach is best-effort/never blocks playback, the
   `AddSlave` calls use proper `file://` URIs, and the PlayerViewModel ctor fan-out updated all construction
   sites (tests compile).
3. Push `feat/subtitle-sidecars`; open the PR; **foreground** `gh pr checks <PR#> --watch` (sleep ~20s
   first); merge `--merge --delete-branch` from the main repo root; sync main; remove the worktree.
4. **Update `ROADMAP.md`** via a **docs branch + PR** (owner rule — never direct-to-main): flip M13 to
   ✅ Merged with the PR #, a one-line summary, and an M13-shipped decision-log entry (durable facts: the
   two `AddSlave` APIs — `Media.AddSlave` before Dispose for auto-detect, `MediaPlayer.AddSlave(...,select)`
   while playing for manual; `SubtitleSidecars` matching rule; `ISubtitleFilePicker`; `HasSubtitleTracks > 1`
   threshold; any STOP-and-report items hit).
5. **Ping** the handoff for planning **M14 (Play-queue & up-next)**.

## STOP-and-report triggers (don't guess)
- `Media.AddSlave(MediaSlaveType, uint, string)` / `MediaPlayer.AddSlave(MediaSlaveType, string, bool)` /
  `MediaSlaveType.Subtitle` differing in the pinned LibVLCSharp 3.9.7.1.
- `IPlaybackEngine`/`LibVlcPlaybackEngine.Load` / `PlayerViewModel` ctor or `_current`/`RefreshTracks`
  shapes differing from the digest.
- `IImagePicker`/`OpenFileDialog` pattern differing such that `ISubtitleFilePicker` can't mirror it.
- The PlayerView subtitle ComboBox not being where the digest says (so the button placement differs).
