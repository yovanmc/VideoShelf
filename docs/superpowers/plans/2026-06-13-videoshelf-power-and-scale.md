# M17 — Power & scale (full icebox §C)

> **Written for Sonnet execution. If something in the codebase doesn't match what this plan says, STOP and report rather than guess.** This plan is fully self-contained: exact files, signatures, and commands are inline. Verify-before-inventing on every API named here (M16 caught the `@`-vs-`$` param trap that way).

**Phase:** v4 / M17 (owner pick #3). **Scope decision (owner, 2026-06-13, batched `AskUserQuestion`):** (1) **FULL icebox §C in one M17, delivered as stacked PRs at the task-group seams** (the M16 model). (2) **Multi-select = BROAD** — creator grid + episode rows + video-card pages (Favorites/Watchlist/Search), all bulk actions (mark watched/unwatched, tag, add-to-playlist/queue, favorite/watchlist, rename). (3) **Cross-series template rename INCLUDED.** (4) **Virtualization = add the `VirtualizingWrapPanel` NuGet** (1.5M downloads, MIT, v2.5.1 Mar-2026, supports .NET 10 WPF) — hosted in a `ListBox` so virtualization + Extended multi-select come from one conversion.

**Test gate:** `dotnet test VideoShelf.slnx -c Release --nologo -v q`
**Quiet build:** `dotnet build VideoShelf.slnx -v minimal`
**Baseline:** 511 tests green at M16 merge (PR #48). Do NOT build on a red baseline — if the worktree baseline isn't green, STOP and report.

---

## ⚠ SIZE & SPLIT SEAMS

This is **~3 milestones' worth** delivered as one M17 per the owner's "full §C" call. It is organized into **9 task groups (A–I)**, each independently shippable, tested, and ending at a clean seam. **Split into stacked PRs at the group boundaries** (`feat/power-scale-a`, `…-b`, …) — do NOT split mid-group. Stack order (each branches off the previous once merged, or off `main` if previous already merged):

1. **A — Selection + virtualization foundation (creator grid)** — the shared primitive; do FIRST.
2. **B — Persistent bulk-action bar + DB-only bulk actions**
3. **C — Multi-select on episode rows + video-card pages** (broadens A's primitive)
4. **D — Command palette (Ctrl+K)**
5. **E — Creator-page accordion virtualization + collapse/expand-all**
6. **F — In-page filter bar + density/list toggle**
7. **G — A–Z jump-list + breadcrumbs**
8. **H — Cross-series template rename** (the only on-disk mutation)
9. **I — Harness `--view` cases + SeedDemo + screenshot sweep + ctor-fan-out consolidation**

If any single group's branch balloons past a reviewable size, that group may itself be split at its sub-task seams, but prefer one PR per group. **Group A is load-bearing** — B/C/D/E/F/G/H all assume the selection model and the `VirtualizingWrapPanel` dep exist.

---

## Architecture at a glance

**New NuGet dep (Group A):** `VirtualizingWrapPanel` (latest 2.5.x; MIT). Add to `src/VideoShelf.App/VideoShelf.App.csproj` only. XAML namespace: `xmlns:vwp="clr-namespace:WpfToolkit.Controls;assembly=VirtualizingWrapPanel"`. This is a **UI-only** dep — it does NOT trip the CI `Assert-NoMediaTools.ps1` denylist (that targets ffmpeg/HandBrake/mkv/x264/… and explicitly allows non-media DLLs). If the `package` CI job flags it, STOP and report (it shouldn't).

**No schema / DB migration.** M17 is **additive UI + VM + reuse of existing repos**; the only new persisted state is two `settings` keys (density + list/grid mode) via the existing `SettingsRepository` key/value pattern (NOT a new column). **The M8→M16 no-`user_version`-runner streak holds — do not add a migration runner.** The sole on-disk mutation (Group H) reuses the existing manifest-undo-safe rename machinery and writes ONLY to library files the user explicitly selected (never creates/deletes outside the existing rename contract).

**New ViewModels:** `SelectionViewModel<T>` (generic selection state, Group A) · `BulkActionBarViewModel` (Group B) · `CommandPaletteViewModel` + `PaletteItemViewModel` (Group D) · `MultiRenameViewModel` (Group H). **Extended ViewModels:** `CreatorCardViewModel`/`RecencyCardViewModel`/`EpisodeViewModel` gain `IsSelected` (Group A/C); `CreatorsViewModel`/`SectionDetailViewModel`/`FavoritesViewModel`/`WatchlistViewModel`/`SearchViewModel` gain a `Selection` + filter/density surface; `MainViewModel` gains palette + breadcrumb + `AppView.MultiRename` host.

**Selection model (the key design):** a per-page `SelectionViewModel<TCard>` exposes `IsSelectionMode` (bool, toggled by a "Select" button in the page header), `SelectedItems` (`ObservableCollection<TCard>`), `SelectedCount`, and `Clear()/SelectAll()/InvertSelection()`. **In selection mode, a card click TOGGLES selection; otherwise it OPENS/PLAYS (today's behavior).** Hosting grid = `ListBox` with `SelectionMode="Extended"` so range (Shift) + Ctrl multi-select are native; each item's `ListBoxItem.IsSelected` is two-way-bound to the card VM's `IsSelected` via an additive `ItemContainerStyle` setter, and the VM's `IsSelected` feeds `SelectedItems`. **When NOT in selection mode, `ListBox` selection is visually suppressed and clicks route to the card's existing `OpenCommand`/`PlayCommand`.**

**Existing facts the executor relies on (verified via digest — re-confirm any that look stale):**
- **Creator grid:** `Views/MainWindow.xaml` ~L142–157 → `<ItemsControl ItemsSource="{Binding Creators.Creators}">` with a `WrapPanel` ItemsPanel and an inline `<views:CreatorCard/>` DataTemplate. `CreatorsViewModel.Creators` is `ObservableCollection<CreatorCardViewModel>`. `CreatorCard.xaml` wraps everything in a single `<Button Command="{Binding OpenCommand}">`.
- **Creator-page accordion:** `Views/SectionDetailView.xaml` ~L153–341, `ItemsControl ItemsSource="{Binding SeriesList}"` + `WrapPanel`. `SeriesViewModel` has `[ObservableProperty] _isExpanded`, `ActivateCommand` → `EnsureEpisodesLoadedAsync()` (lazy episode load on first expand), `Episodes` collection. `SectionDetailViewModel.SeriesList` is `ObservableCollection<SeriesViewModel>`; it already has `[RelayCommand] MarkCreatorWatched/Unwatched` (L41/L48) calling `WatchRepository.SetWatchedForSection`.
- **Cards:** `VideoCard.xaml` = `<Button Command="{Binding PlayCommand}">`; `CreatorCard.xaml` = `<Button Command="{Binding OpenCommand}">`. Both are `UserControl`s. `EpisodeViewModel` has `Watched`/`IsFavorite`/`InWatchlist`/`Rating` (no `IsSelected` yet). `RecencyCardViewModel.ThumbnailPath` is populated NOWHERE (M16 note — video cards render placeholder only; not an M17 concern, do not "fix" it here).
- **Only existing selection pattern:** `TagChipViewModel.IsSelected` (bool) bound `OneWay` to a `ToggleButton.IsChecked` (`DiscoveryView.xaml` L304). No `ListBox.SelectedItems`, `ICollectionView`, or `CollectionViewSource` anywhere yet.
- **Top chrome:** `MainWindow.xaml` ~L44–127 — Back (`GoBackCommand`/`CanGoBack`) · Home · Browse · **Library `Menu`** (SmartViews/Playlists/Watchlist/Favorites/History/SurpriseMe) · persistent search `TextBox` (binds `Search.Query`, L104) · Up-next (`PlayQueue.HasQueue`) · Settings. Active-underline = a sibling `Border Height="2"` gated by `EnumSetToVisibility` (converter key `EnumSetToVis`, comma-set; `Converters.cs` ~L19–31) over `CurrentView`. **No global `InputBinding`/`KeyBinding` infra exists** — Ctrl+K must be added (see Group D).
- **Search:** `SearchViewModel` — `CreatorResults: ObservableCollection<CreatorCardViewModel>`, `VideoResults: ObservableCollection<RecencyCardViewModel>`, 150ms debounce; calls `LibraryRepository.SearchCreators(query, limit) → IReadOnlyList<SectionSummary>` (L357) and `SearchVideos(query, limit) → IReadOnlyList<RecencyItem>` (L396).
- **MainViewModel commands** (palette action registry source): `ShowHome`/`ShowBrowse`/`ShowSettings`/`ShowQueue`/`ShowSmartViews`/`ShowFavorites`/`ShowWatchlist`/`ShowPlaylists`/`ShowHistory`/`GoBack`/`SurpriseMe`/`TogglePictureInPicture`/`ClosePlayer`/`ScanAndReload` (all `[RelayCommand]`); `OpenSectionAsync(long)`, `PlayEpisode(EpisodeView)`, `OpenRenameToolAsync(SeriesViewModel)`.
- **Bulk DB primitives:** `WatchRepository.SetWatched(long videoId, bool)` (single), `SetWatchedForSeries(long, bool)`, `SetWatchedForSection(long, bool)` — marking watched=true also inserts a `watch_events` row + clears `resume_position`/`resume_updated_at`; unwatch is `watched=0` only. `TagRepository` (params are **`@`-prefixed, NOT `$`**): `AddVideoTag(videoId, tag)`/`RemoveVideoTag`, `Normalize(tag)` static. `CurationRepository.SetFavorite(videoId, bool)` / `SetWatchlist(videoId, bool, DateTimeOffset now)`. `PlaylistRepository.AddItem(playlistId, videoId)` / `GetAll() → IReadOnlyList<Playlist>`. `PlayQueueViewModel` — has `Enqueue`/`StartSingle`; confirm the exact enqueue method name before wiring (STOP-and-report if it differs).
- **Rename:** `VideoShelf.Core.Renaming` — `CanonicalNamer.Build(string baseTitle, int? episodeNo, string ext, int padWidth)`, `PadWidth(IEnumerable<int>)`, `SanitizeTitle(string)`. `RenamePlanner(IFileSystem).BuildPlan(IReadOnlyList<Video> videos, IReadOnlyDictionary<long,string> proposedNames) → RenamePlan` (statuses Unchanged/Ready/SourceMissing/TargetExists/DuplicateTarget/InvalidName). `RenameExecutor(IFileSystem, LibraryRepository).Apply(RenamePlan, long seriesId, string manifestDir) → RenameResult` (writes JSON manifest FIRST, 2-arg `File.Move` never overwrites, re-verifies at apply) and `.Undo(string manifestPath)`. `RenameManifest(BatchId, SeriesId, CreatedAtUtc, Entries[])` with `RenameManifestEntry(VideoId, OldPath, NewPath)`. `LibraryRepository.UpdateVideoPath(videoId, oldPath, newPath)` (updates `file_path` + `raw_filename` + path-keyed `grouping_overrides` in one tx). `RenameToolViewModel.LoadAsync(long seriesId, string baseTitle, bool isStandalone)` loads via `GetVideosForSeries(seriesId)`; manifest path persisted in `settings` key `last_rename_manifest`.
- **DI:** `Services/ServiceCollectionExtensions.cs` `AddVideoShelf(this IServiceCollection, string? dataDirOverride = null)` — all repos + VMs are `AddSingleton`. `PlayerViewModel` built via a factory lambda injecting `AppPaths` dirs.
- **Test factory:** `tests/VideoShelf.App.Tests/TestSupport/MainViewModelTestFactory.cs` `Create(out MainVmContext ctx)`; constructs all VMs with real repos over an in-memory test DB + fakes (`InMemoryFileSystem`, `FakePlaybackEngine`, `NullThumbs`, `NullScan`, `FakeMediaProbe`). **Nullable-trailing-param ctor pattern (M16):** new optional deps go on child VMs as trailing `= null` params so the ~12 existing construction sites compile unchanged; only the production chain threads real instances.
- **Theming rule (hard):** never override/re-base a **WPF-UI** themed control's Style/Template for cosmetics (caused 2 sibling regressions). `ListBox`/`ListBoxItem`/`CheckBox` styled here are **plain WPF** controls, so an additive `ItemContainerStyle` on them is allowed — but keep changes additive (set `Template`/`Background`/`Padding` only as needed for the card-grid look; do NOT retemplate any `ui:*` control). When in doubt, STOP-and-report.
- **Sweep mechanics:** `Run-VisualSweep.ps1` (pwsh 7, **unlocked composited desktop**, TOPMOST→NOTOPMOST toggle, ~5s Mica settle); launch hooks `--folder`/`--autostart`/`--done-signal`/`--data-dir`/`--view`/`--play`/`--seed-demo`; PNGs viewed by a **Sonnet subagent returning a TEXT verdict** — never load PNGs into the controller. Close stray always-on-top media windows (the recurring "Webcam Streams Recorder"/flet + League bleed class) before trusting a grab. Tall views need a scrolled multi-shot.

**Conventions:** worktrees under `.worktrees/`; `gh` at `& "C:\Program Files\GitHub CLI\gh.exe"`; commit author `yovanmc` + trailer `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>` (no Codex trailer); merge `--merge` (no squash) from the **main repo root**; remove the worktree before `git branch -D`; the ROADMAP flip rides the LAST group's branch (or a final docs commit).

---

## Group A — Selection + virtualization foundation (creator grid)

**Foundation. Do FIRST. Everything else assumes this exists.** Ships: a virtualized, multi-selectable Browse creator grid + the reusable selection primitive.

### A0 — Add the NuGet
- `dotnet add src/VideoShelf.App/VideoShelf.App.csproj package VirtualizingWrapPanel` (pin to the latest 2.5.x that restores; mirror the libVLC "pin what restores" discipline). Run the gate build; confirm restore is clean.

### A1 — `SelectionViewModel<T>` (generic, reusable)
New `src/VideoShelf.App/ViewModels/SelectionViewModel.cs`:
```csharp
public partial class SelectionViewModel<T> : ObservableObject where T : ISelectableCard
{
    [ObservableProperty] private bool _isSelectionMode;
    public ObservableCollection<T> SelectedItems { get; } = new();
    public int SelectedCount => SelectedItems.Count;
    public bool HasSelection => SelectedItems.Count > 0;

    // Called by the item container when its IsSelected flips (Group A2 binding).
    public void OnItemSelectionChanged(T item) { /* add/remove in SelectedItems; raise SelectedCount/HasSelection */ }
    [RelayCommand] private void EnterSelectionMode() { IsSelectionMode = true; }
    [RelayCommand] private void ExitSelectionMode() { IsSelectionMode = false; ClearSelection(); }
    [RelayCommand] private void ClearSelection() { /* set each .IsSelected=false; clear SelectedItems */ }
    [RelayCommand] private void SelectAll(IEnumerable<T> all) { /* set IsSelected=true on all */ }
    [RelayCommand] private void InvertSelection(IEnumerable<T> all) { /* flip each */ }
}
public interface ISelectableCard { bool IsSelected { get; set; } }
```
- Add `bool IsSelected` (`[ObservableProperty]`) to `CreatorCardViewModel` and implement `ISelectableCard`. The `OnIsSelectedChanged` partial calls back into the owning `SelectionViewModel` — wire via an `Action<bool>? SelectionChanged` callback set when the card is created, OR have `CreatorsViewModel` subscribe to each card's `PropertyChanged`. **Pick the subscription approach that avoids leaking a back-ref into the card** (mirror the M14 "SeriesViewModel gets no queue ref; bubble via events/Owner-less" preference). STOP-and-report if neither is clean.

### A2 — Convert the Browse creator grid to a virtualized, selectable `ListBox`
In `MainWindow.xaml`, replace the creator `ItemsControl`+`WrapPanel` with:
```xml
<ListBox ItemsSource="{Binding Creators.Creators}"
         SelectionMode="Extended"
         ScrollViewer.CanContentScroll="True"
         VirtualizingPanel.IsVirtualizing="True"
         VirtualizingPanel.VirtualizationMode="Recycling"
         Style="{StaticResource PlainListBox}">      <!-- additive: strips ListBox chrome -->
  <ListBox.ItemsPanel>
    <ItemsPanelTemplate><vwp:VirtualizingWrapPanel SpacingMode="Uniform"/></ItemsPanelTemplate>
  </ListBox.ItemsPanel>
  <ListBox.ItemContainerStyle>
    <Style TargetType="ListBoxItem" BasedOn="{StaticResource PlainListBoxItem}">
      <Setter Property="IsSelected" Value="{Binding IsSelected, Mode=TwoWay}"/>
    </Style>
  </ListBox.ItemContainerStyle>
  <ListBox.ItemTemplate><DataTemplate><views:CreatorCard/></DataTemplate></ListBox.ItemTemplate>
</ListBox>
```
- Add `PlainListBox`/`PlainListBoxItem` styles to `DesignTokens.xaml` (or a new `Resources/SelectionStyles.xaml` merged after DesignTokens): transparent `Background`, no `BorderThickness`, retemplate `ListBoxItem` to just a `ContentPresenter` + a selection overlay (a `Border` whose `BorderBrush=AccentBrush`/`Background=SelectionHighlightBrush` is visible only when `IsSelected` **and** the page is in selection mode). Add tokens `SelectionHighlightBrush` (~`#332C9CFF` — low-alpha accent) and `CheckboxSize`=20. **These are plain-WPF `ListBox`/`ListBoxItem` styles → additive retemplate is allowed; do NOT touch any `ui:*` template.**
- **Click routing:** when NOT in selection mode, suppress the ListBox selection visual and let the `CreatorCard`'s inner `OpenCommand` fire (it still navigates). When IN selection mode, a card click should toggle selection, not navigate — gate `CreatorCard`'s `OpenCommand` on `IsSelectionMode==false` (bind the card's `Button.IsHitTestVisible` or route through a wrapper). **This click-routing reconciliation is the riskiest UI bit in Group A — STOP-and-report with the two candidate approaches (a) disable the inner Button's command in selection mode + let ListBoxItem handle the click, vs (b) a checkbox overlay that only appears in selection mode and owns the toggle — rather than rebuild-guessing.**
- Add a **"Select" toggle button** to the Browse header (enters/exits selection mode) + a "Select all / Clear" affordance shown only in selection mode.
- `CreatorsViewModel` gains `public SelectionViewModel<CreatorCardViewModel> Selection { get; }`.

### A3 — Tests
- `SelectionViewModelTests`: enter/exit mode, toggle item → `SelectedItems`/`SelectedCount`/`HasSelection`, SelectAll/Invert/Clear. Pure VM, no UI.
- `CreatorCardViewModelTests`: `IsSelected` round-trips and notifies.
- **Expected +~10 Core/App tests.** XAML (the ListBox swap, click routing) is **screenshot-verified in Group I**, not unit-tested (the standing libVLC/WPF testability rule: logic in VMs with fakes; views are integration).

**Seam.** Ships virtualized + selectable Browse grid (no bulk actions yet).

---

## Group B — Persistent bulk-action bar + DB-only bulk actions

Ships: a bottom action bar that appears when a selection is non-empty, wiring the no-file-write bulk actions over the selected creators.

### B1 — `BulkActionBarViewModel`
New `src/VideoShelf.App/ViewModels/BulkActionBarViewModel.cs`. It is **selection-source-agnostic**: constructed with the repos it needs (`WatchRepository`, `TagRepository`, `CurationRepository`, `PlaylistRepository`, `PlayQueueViewModel`, `LibraryRepository`) and given the current selection as `IReadOnlyList<long> videoIds` (resolved by the page — see B3). Commands:
- `MarkWatched` / `MarkUnwatched` → per id `WatchRepository.SetWatched(id, true/false)` (reuse the single-video method; it already handles `watch_events`+resume side-effects — do NOT reimplement). **STOP-and-report: confirm `SetWatched`'s exact side-effects before looping it** (the M16 watch-history STOP flag — don't guess event/resume behavior).
- `AddTag(string tag)` → `TagRepository.Normalize` then `AddVideoTag(id, tag)` per id (params are `@`-prefixed). A small inline tag-entry (reuse the `TagEditorViewModel` input idiom or a plain prompt; do NOT open the full creator-page tag editor).
- `AddToPlaylist(long playlistId)` → `PlaylistRepository.AddItem(playlistId, id)` per id; expose `AvailablePlaylists` from `PlaylistRepository.GetAll()`.
- `AddToQueue` → `PlayQueueViewModel.Enqueue(...)` per resolved `EpisodeView` (confirm the enqueue API).
- `SetFavorite(bool)` / `SetWatchlist(bool)` → `CurationRepository.SetFavorite`/`SetWatchlist(id, value, now)` per id (`now` passed in — never `DateTime.Now` in Core).
- After any action: raise a `Completed` event so the page can refresh affected rows + (optionally) exit selection mode.

### B2 — Bar UI
- A `BulkActionBar` UserControl (`Views/BulkActionBar.xaml`): a bottom-docked opaque `Border` ("N selected" + the action buttons + a Clear/✕). Host it in `MainWindow.xaml` as a bottom overlay, visible when the active page's `Selection.HasSelection` is true. Use `ui:SymbolIcon`s consistent with M15 (Checkmark/Tag/List/Heart/etc.; only grep-proven symbols — wrong member fails compile, pick nearest). Opaque per the M10 transparency-renders-black trap if it ever overlaps the player (it won't in selection contexts, but keep it opaque).

### B3 — Creator-selection → video-id resolution
A selected **creator** (section) expands to its video ids. Add `LibraryRepository.GetVideoIdsForSection(long sectionId) → IReadOnlyList<long>` (non-missing only) if no equivalent exists (confirm first — `GetEpisodesForSection` from M14 may suffice; prefer reusing it). The Browse page maps `Selection.SelectedItems` (creators) → flattened video ids → feeds `BulkActionBarViewModel`.

### B4 — Tests
- `BulkActionBarViewModelTests` with real repos over the in-memory DB + a fake queue: each action mutates exactly the selected ids; mark-watched clears resume + writes one event per id; tags/favorite/watchlist/playlist land. **Expected +~12 Core/App tests.**

**Seam.**

---

## Group C — Multi-select on episode rows + video-card pages

Broadens the Group A primitive to the rest of the broad-scope surfaces.

### C1 — Episode rows (creator-page accordion)
- Add `[ObservableProperty] bool IsSelected` to `EpisodeViewModel` + `ISelectableCard`. `SeriesViewModel` (or `SectionDetailViewModel`) gains a `SelectionViewModel<EpisodeViewModel>` spanning the section's episodes. A "Select" mode on the creator page shows a checkbox per episode row; the bulk bar resolves the selected `EpisodeViewModel`s' video ids directly.
- Reuse the **same** `BulkActionBarViewModel` (it's id-based). The creator page feeds it episode-level ids.

### C2 — Video-card pages (Favorites / Watchlist / Search)
- Convert the `FavoritesView`/`WatchlistView`/`SearchView` grids (today `ItemsControl`+`WrapPanel` over `RecencyCardViewModel`/`CreatorCardViewModel`) to the **same `ListBox`+`VirtualizingWrapPanel`+`SelectionViewModel` pattern** from Group A. Add `IsSelected`/`ISelectableCard` to `RecencyCardViewModel`. Each page gets a `Selection` + the shared bulk bar (resolving the cards' video ids).
- Search has both creators and videos — selection on the **video** group (and optionally creators); keep it simple: selection per result group.

### C3 — Tests
- `EpisodeViewModelTests` (IsSelected), per-page selection wiring (factory-constructed VMs). **Expected +~10 tests.**

**Seam.**

---

## Group D — Command palette (Ctrl+K)

Ships: a global fuzzy "jump to anything" overlay.

### D1 — `CommandPaletteViewModel` + `PaletteItemViewModel`
New files. The palette aggregates three result kinds into one ranked list:
- **Actions** — a static registry of `(label, icon, Action)` built from `MainViewModel`'s navigation commands (Home/Browse/Settings/SmartViews/Playlists/Watchlist/Favorites/History/Up-next/Surprise-me/Scan). Build the registry in `MainViewModel` (it owns the commands) and pass it in.
- **Creators** — `LibraryRepository.SearchCreators(query, limit)` → navigate via `OpenSectionAsync(id)`.
- **Videos** — `LibraryRepository.SearchVideos(query, limit)` → `PlayEpisode`/navigate.
- Fuzzy ranking: a small pure scorer (subsequence match + prefix/word-boundary bonus) in Core or a `PaletteRanker` static — **unit-test it** (deterministic). Debounce queries (~120ms, mirror SearchViewModel). `[RelayCommand] Execute(PaletteItemViewModel)` runs the item's action and closes the palette; arrow keys move selection, Enter executes, Esc closes.

### D2 — Hotkey + overlay
- **There is no global hotkey infra** — add an `InputBinding` (`<KeyBinding Modifiers="Ctrl" Key="K" Command="{Binding OpenCommandPaletteCommand}"/>`) to `MainWindow`'s `Window.InputBindings`, plus a `PreviewKeyDown` fallback in `MainWindow.xaml.cs` if the focus-scoped InputBinding doesn't fire while a TextBox has focus (STOP-and-report if KeyBinding alone proves unreliable). `OpenCommandPalette` on `MainViewModel` shows the overlay + focuses its TextBox.
- Overlay = a centered `Popup`/`Grid` over the content (opaque card, dimmed scrim), `IsCommandPaletteOpen` gated. Esc/click-scrim closes. Do NOT block player input when the player is active (or simply allow palette over any non-player view; STOP-and-report if player interaction is unclear).

### D3 — Tests
- `PaletteRankerTests` (ranking/ordering), `CommandPaletteViewModelTests` (query → mixed results, Execute routes to the right action, Esc closes). **Expected +~12 tests.**

**Seam.**

---

## Group E — Creator-page accordion virtualization + collapse/expand-all

Ships: the 40+-series wall scrolls smoothly + collapse/expand-all.

### E1 — Collapse/Expand-all (low risk, do first)
- `SectionDetailViewModel`: `[RelayCommand] ExpandAll()` / `CollapseAll()` loop `SeriesList` toggling `IsExpanded` (ExpandAll triggers lazy `EnsureEpisodesLoadedAsync` per series — guard against a thundering load; consider expand-all only sets `IsExpanded` and lets each tile load on demand, or batch-loads with a single combined query — STOP-and-report if expand-all causes a noticeable stall at 40+ series). Buttons in the creator-page header.

### E2 — Virtualize the series grid (HARDER — flagged)
- **Conflict:** `VirtualizingWrapPanel` virtualizes uniform-ish items, but the accordion tiles change height in place when expanded → wrap + variable-height + virtualization is genuinely fragle. **STOP-and-report and choose with the owner if it's non-trivial.** Candidate approaches, in preference order:
  1. **Virtualize only the collapsed grid; render expanded episodes in a separate non-virtualized region.** Clicking a tile could navigate to an expanded series view (or push the episodes into a side/detail panel) instead of expanding in place — but that changes the M9 in-place-accordion UX, so **only do this with owner sign-off.**
  2. **Keep in-place accordion; rely on `VirtualizingWrapPanel`'s variable-size support** (the library claims it) with `VirtualizationMode=Standard` (not Recycling, which fights variable heights). Test at 40+ series with several expanded.
  3. **Defer E2** (ship E1 collapse/expand-all + the in-page filter from Group F as the scale mitigation; leave the grid non-virtualized). The icebox lists virtualization as `[H/M]` — if 1 and 2 both prove risky, deferring E2 with a logged note is acceptable per the "no silent caps — log what was dropped" discipline.
- Whatever path: **document the decision in the PR + ROADMAP decision log.**

### E3 — Tests
- `SectionDetailViewModelTests`: ExpandAll/CollapseAll flip all `IsExpanded`; expand-all loads episodes. **Expected +~6 tests.**

**Seam.**

---

## Group F — In-page filter bar + density/list toggle

### F1 — Ad-hoc in-page filter bar
- Browse: a live text filter over `Creators.Creators` (name + tags). Creator page: a live filter over `SeriesList` (series title). Implement with an `ICollectionView` (`CollectionViewSource.GetDefaultView(collection)` + `.Filter`) so it composes with virtualization and doesn't mutate the source collection — **this is the first `ICollectionView` use in the repo; STOP-and-report if it fights the `ListBox`/`VirtualizingWrapPanel`** (it shouldn't — VWP supports `CollectionView`). A filter `TextBox` in the page header, collapsed by default behind a filter toggle, with a clear-✕.
- Distinguish from M16's **saved** smart-view builder (persisted, on Home) — this is **ephemeral, in-page, live**. Don't touch smart-view code.

### F2 — Density toggle + list/grid toggle
- Density (Compact / Normal / Spacious) scales card sizing + gap. Implement via swappable token sets: define `CardWidth`/`CardThumbHeight`/`CardGap` variants and a `DensityToBrush`/`DensityToDouble`-style converter, OR three `Style` variants selected by a VM `Density` enum. Persist the choice in `settings` (key e.g. `browse_density`) via `SettingsRepository` (NOT a new column — reuse the key/value `Get/Set` idiom; confirm the method names, e.g. `GetString`/`SetString`).
- List vs grid: a `ViewMode` enum (Grid/List); List mode swaps the `ItemTemplate` to a compact row (thumbnail + title + count/metadata + the same context menu) and the `ItemsPanel` to a `VirtualizingStackPanel`. Persist in `settings` (`browse_view_mode`).
- A small toggle group in the Browse header. Apply consistently where it makes sense (Browse first; Favorites/Watchlist optional).

### F3 — Tests
- `SettingsRepository` round-trip for the two new keys; VM filter logic (`ICollectionView` filter predicate is pure — unit-test the predicate, not the view). VM `Density`/`ViewMode` persistence. **Expected +~10 tests.**

**Seam.**

---

## Group G — A–Z jump-list + breadcrumbs

### G1 — A–Z jump-list (Browse)
- A vertical A–Z index strip beside the creator grid; clicking a letter scrolls the `ListBox` to the first creator whose name starts with that letter (`ListBox.ScrollIntoView` on the first match, or `VirtualizingWrapPanel`'s `BringIndexIntoView`). Disable letters with no matches. Pure helper `JumpListIndex.FirstIndexForLetter(items, letter)` — unit-test it.

### G2 — Breadcrumbs
- A breadcrumb row (new `Grid` row between chrome and content in `MainWindow.xaml`, or a header band per detail view): `Home / Browse > {Creator} > {Series}`, each segment clickable (navigates back up; reuse `ShowBrowse`/`OpenSectionAsync`/back-stack). Visible only on SectionDetail (and deeper) — gate with `EnumSetToVis` over `CurrentView` (reuse the existing converter). Keep it additive; do not disturb the active-underline nav.

### G3 — Tests
- `JumpListIndexTests`; breadcrumb segment model/VM if any. **Expected +~6 tests.**

**Seam.**

---

## Group H — Cross-series template rename (the only on-disk mutation)

Ships: rename a multi-series selection with a `{creator} - {series} - {NN}` template, reusing the manifest-undo-safe machinery. **This is the only group that writes to library files — preserve every safety property of the existing rename contract.**

### H1 — Template tokens in Core
- Extend `CanonicalNamer` with a pure template renderer: `RenderTemplate(string template, TemplateContext ctx, int? episodeNo, string ext, int padWidth) → string`, where tokens are `{creator}`, `{series}`, `{NN}` (zero-padded episode), and literals pass through; sanitize the final name via the existing `SanitizeTitle`. `TemplateContext(string Creator, string Series)`. Keep it a **pure, exhaustively unit-tested** function (mirror M16's `SmartViewSqlBuilder` rigor). The default template is `"{series} {NN}"` (reproduces today's canonical per-series behavior so single-series rename is a special case). **Crucially: the rendered name must still re-parse to a stable `(title, episode)` via `TitleParser`** for rescan-stability — add a test asserting round-trip for the default + the `{creator} - {series} - {NN}` template; STOP-and-report if a chosen template breaks re-parse.

### H2 — `MultiRenameViewModel` + `AppView.MultiRename`
- New VM mirroring `RenameToolViewModel` but spanning a **selection of series** (or videos): `LoadAsync(IReadOnlyList<long> seriesIds, string template)`. For each series it pulls `GetVideosForSeries`, resolves the creator/series names, renders proposed names, and builds **one combined** `RenamePlan` across all videos (the planner already de-dups/cross-checks targets — feed it the full `IReadOnlyDictionary<long,string>` and the full `IReadOnlyList<Video>`). Live re-plan on template edit. Per-row editable override (same as the single tool). `Apply` writes **one manifest** covering all series and calls `RenameExecutor.Apply` — **STOP-and-report: `RenameExecutor.Apply(plan, seriesId, manifestDir)` is keyed on a single `seriesId`; confirm whether the manifest/`UpdateVideoPath` path is per-video (id-based, so multi-series is fine) or assumes one series. If it assumes one series, extend it to accept the full plan without a single seriesId rather than calling it N times** (N manifests would make Undo non-atomic — we want ONE undo manifest for the batch). Persist the batch manifest path in `settings.last_rename_manifest` (single-slot undo, same as today).
- Entry point: the **bulk-action bar** "Rename…" button (Group B) when the selection resolves to series; it opens `AppView.MultiRename` seeded with the selected series ids + a default template.

### H3 — Tests
- `CanonicalNamer` template tests (token rendering, padding, sanitize, **re-parse round-trip**). `RenamePlanner` over a multi-series video set (cross-series target collisions flagged `DuplicateTarget`). `MultiRenameViewModel` over the in-memory DB + `InMemoryFileSystem`: preview → apply → `UpdateVideoPath` for every id → one manifest → Undo restores all. **Expected +~16 tests.** Reuse `InMemoryFileSystem` (shared from Core.Tests via the existing `<ProjectReference>`).

**Seam.**

---

## Group I — Harness, sweep, ctor consolidation

### I1 — Ctor fan-out consolidation
- Consolidate any `MainViewModel`/child-VM ctor growth from A–H here (palette registry, bulk bar, multi-rename host). Use the **nullable-trailing-param pattern** on child VMs; thread real instances only in the production chain + the test factory. Update `MainViewModelTestFactory` + affected test construction sites in one pass.

### I2 — Harness `--view` cases + SeedDemo
- Add `--view` cases for the new/changed surfaces: a selection-mode Browse grid (with the bulk bar visible — seed a couple of selected creators), the command palette (open, with a query pre-filled), the in-page filter bar, density=Compact, the multi-rename preview. Extend `HarnessRunner` + `HarnessOptions` (the parser ignores unknown args — forward-compatible). **SeedDemo must seed ENOUGH creators/series** to make virtualization, A–Z, and collapse/expand-all meaningful (e.g. ≥30 creators spanning the alphabet, one creator with ≥40 series).

### I3 — Screenshot sweep + Sonnet verdict
- Run `Run-VisualSweep.ps1` (unlocked desktop; close stray media windows). A **Sonnet subagent** views the PNGs and returns a TEXT verdict + paths against per-surface acceptance criteria. Tall views (Browse grid, creator page) need a scrolled multi-shot. Fix findings additively only. Home shelves/below-fold caveat applies — verify-by-proxy via the dedicated pages + VM tests where a static grab can't reach.

### I4 — ROADMAP flip
- Flip the M17 row to ✅ Merged with the PR list + one-line summary; append a decision-log entry (durable gotchas: the click-routing approach chosen, the accordion-virtualization decision from E2, the multi-rename manifest shape from H2, the NuGet pin, any deferred items). Rides this final branch.

---

## Acceptance criteria (milestone)

1. **Browse creator grid is virtualized** (scrolls smoothly at 40+ creators — verified in the sweep / by the VWP swap) and supports **range + Ctrl multi-select** in selection mode; normal-mode clicks still open the creator.
2. **A persistent bulk-action bar** appears on non-empty selection and performs **mark watched/unwatched, bulk-tag, add-to-playlist, add-to-queue, favorite, watchlist** over the selected items, mutating exactly those ids (mark-watched preserves the single-method side-effects).
3. **Multi-select works on episode rows and the Favorites/Watchlist/Search video-card pages** (broad scope) with the same bar.
4. **Ctrl+K opens a command palette** that fuzzy-matches creators, videos, and navigation actions; Enter executes/navigates, Esc closes.
5. **Creator page has collapse-all / expand-all**; the series grid scales (virtualized, or the E2 decision logged if deferred).
6. **An in-page live filter bar** filters the current grid ephemerally (distinct from saved smart views), and a **density toggle** + **list/grid toggle** are persisted in settings.
7. **A–Z jump-list** scrolls the creator grid to a letter; **breadcrumbs** show the Home/Browse > Creator > Series path and navigate up.
8. **Cross-series template rename** (`{creator} - {series} - {NN}`) over a multi-series selection produces ONE undo manifest, repaths every id via `UpdateVideoPath`, never overwrites, and Undo restores the whole batch; rendered names re-parse to stable `(title, episode)`.
9. **No schema migration / no `user_version` runner**; only two new `settings` keys. **No WPF-UI control retemplated** (plain-WPF `ListBox`/`ListBoxItem`/`CheckBox` styles are additive and OK). **Library read-only invariant holds** except the explicit Group H rename (same contract as M5).
10. **Gate green** (`dotnet test VideoShelf.slnx -c Release --nologo -v q`) and **screenshot sweep PASS** (Sonnet text verdict) on the new/changed surfaces. The real gate is green + sweep PASS, not a test count.

## Out of scope (deferred, documented)
- **Series grouping override (split/merge) + manual episode order** → **M18** (Library health) per the 2026-06-13 refine dedup.
- **Resolution-axis filtering** → needs the M18 probe extension (no width/height captured yet).
- **Per-shelf collapse on Home** — low value; not in §C.
- **E2 accordion virtualization** may ship deferred if both in-place approaches prove risky (log it; collapse/expand-all + in-page filter are the fallback scale mitigation).

## Estimated test delta
Indicative: ~+100 across groups (A ~10, B ~12, C ~10, D ~12, E ~6, F ~10, G ~6, H ~16, + I wiring) → from **511** toward **~610**. The gate is green + sweep PASS, not the number.
