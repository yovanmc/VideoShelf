# M24 — Lean + Refresh (cut bloat · black-glass · insights · creator portraits) — Implementation Plan

> **Written for Sonnet execution.** This plan touches existing files digested by signature, not read line-by-line. For every task that EDITS or REMOVES an existing file: **read the current file first.** If its actual shape does not match what this plan describes, **STOP and report** rather than forcing the change. Removals especially: if a feature is entangled with a KEPT feature, STOP and report rather than ripping out something load-bearing.

**Goal:** The owner reviewed the whole feature set and chose to **cut bloat and fold it into M24** alongside the visual refresh + the two new features. One milestone, stacked PRs at group seams (M16/M23 model). Net result: a leaner, focused personal-library player on a black-glass theme, plus insights and creator portraits.

**Owner-decided scope (locked via a live review):**
- **CUT:** Smart Views (rule builder + page + Home shelves) · Command palette (Ctrl+K) · Surprise Me · player extras (aspect/zoom presets, volume normalization, the standalone frame-screenshot button, now-playing-in-titlebar, PiP snap-to-corner animation, series-complete celebration, card→hero crossfade) · accessibility remainder (keyboard-nav helpers, focus-restore service, type-ahead, color-independent ✓/NN% cues, 44px hit-target minimums) · series **split/merge** · **cross-series + per-series batch** rename.
- **KEEP confirm/undo + tooltips** (NOT cut — data safety + universal UX).
- **TRIM:** rename → keep ONLY single-file rename; series editing → keep ONLY move-episode-in/out + manual reorder.
- **RENAME:** Watchlist → **"Watch Later"** (nav label, page title, the per-video toggle/command, any tooltips).
- **ADD (the original M24 prongs):** black-glass refresh (Ice Cyan `#4FC3F7` on true-black `#070707`) · insights dashboard · creator-portrait-from-a-frame (offline hybrid picker).
- **KEEP (explicitly affirmed, do NOT touch):** Playlists · Watch Later · recommendation rails + tags · favorites + ratings · PiP · play queue + up-next · A-B loop · speed · nav niceties (A–Z jump-list, breadcrumbs, scroll memory) · full maintenance suite (relink, remove-source, duplicate detect/resolve, orphan/empty cleanup, health dashboard).

**Architecture:** 7 stacked PRs at group seams: **A** black-glass refresh → **B** cut Smart Views + palette + Surprise Me → **C** cut player extras + accessibility remainder → **D** trim rename + grouping + rename Watchlist → **E** insights → **F** creator portraits → **G** reduced UX polish + ROADMAP flip. Each removal PR must leave the app building, launching, and green; remove features end-to-end (VM + view + repo methods + DI + nav/menu entries + harness `--view` states + tests) and delete now-dead code.

**No `user_version` runner / no schema change.** Removals are code-only (orphaned tables like `watch_events` references stay — do NOT drop tables/columns; just stop using the removed UI). New features reuse existing tables/columns. The M8→M23 no-runner streak holds. No `ui:*` retemplate.

**Tech stack:** .NET 10 · WPF + WPF-UI (`ui:FluentWindow`, Mica) · LibVLCSharp · `Microsoft.Data.Sqlite` · xUnit/Shouldly. `gh` is **not on PATH** → `& "C:\Program Files\GitHub CLI\gh.exe"`. Solution: `VideoShelf.slnx`.

---

## Conventions (apply to every task)

- **Test gate** (after every group, before every PR): `dotnet test VideoShelf.slnx -c Release --nologo -v q` → `Failed: 0`. Baseline **1060**. Removal PRs will REMOVE tests for cut features (expected — the total may drop); ADD PRs climb. State the new total each time. Core flake → re-run Core alone.
- **Build quietly:** `dotnet build VideoShelf.slnx -c Release -v minimal`.
- **Removals are deletions, not dead code:** delete the cut feature's files (VMs/views/repos), its DI registrations, its `AppView` enum members + `MainViewModel` commands/properties, its nav-menu items + content hosts + active-underline entries, its `Run-VisualSweep.ps1` `--view` entries + `HarnessRunner` cases, and its tests. After each removal, **grep for the removed type/command names to confirm zero dangling references** before building.
- **Commits:** author `yovanmc`, trailer `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`. **No Codex trailer.** Merge `--merge`. `gh pr merge` from the repo root. Direct pushes to `main` blocked. ROADMAP flip rides G.
- **CI:** push branch + open PR → sleep ~20s → `& "C:\Program Files\GitHub CLI\gh.exe" pr checks <PR#> --watch` foreground → merge when green.
- **Theming (LOAD-BEARING):** additive only; never set `Style`/`Template` on a `ui:*` control; never alias a WPF-UI brush via nested `<StaticResource ResourceKey=…>` then consume as `{StaticResource}` (make new tokens DIRECT brushes). [[wpfui-theming-and-visual-verification]]
- **Verification:** `Run-VisualSweep.ps1` writes PNGs to `tests/screenshots/<stamp>/`; a **Sonnet subagent views them → TEXT verdict** (never load PNGs into the controller [[feedback-screenshot-verify-in-subagent]]). Over-video/libVLC content is GDI-uncapturable (verify-by-proxy). The sweep's real-app `--view` launches are the render-crash backstop. After REMOVALS, the sweep also confirms no orphaned nav entry / blank page remains. Group A note: the GDI sweep can false-FAIL small/dense UI at full-window zoom — crop+upscale before judging (M23 Group C lesson).
- **Accessibility:** the owner has now asked to REMOVE the accessibility remainder (Group C) — keyboard nav, focus-restore, type-ahead, color ✓/% cues, 44px minimums. Do NOT re-introduce any `AutomationProperties`/screen-reader (PR #77 + this). KEEP confirm/undo + tooltips.

---

## Group A — Black-glass refresh (Ice Cyan on true-black) — PR #1

> Owner picked "Ice Cyan `#4FC3F7` on true-black `#070707`" from a live palette exploration. Base today is the WPF-UI **Mica** backdrop (~`#1C1C1C`); `SurfaceBrush`/`CardSurfaceBrush` are **aliases** to WPF-UI keys, so a color edit alone won't work — Mica must be off and the surface tokens made direct.

### A1 — true-black base
**Files:** `src/VideoShelf.App/Resources/DesignTokens.xaml`, `src/VideoShelf.App/Views/MainWindow.xaml`.
- [ ] **Read `DesignTokens.xaml` first.** Convert the two aliases to DIRECT brushes (also fixes the nested-`<StaticResource>` fragility): `SurfaceBrush` → `<SolidColorBrush x:Key="SurfaceBrush" Color="#FF070707" />`; `CardSurfaceBrush` → `<SolidColorBrush x:Key="CardSurfaceBrush" Color="#FF141414" />` (subtle elevation). Leave text/divider/chip/player-scrim brushes unchanged.
- [ ] **Read `MainWindow.xaml` (the `ui:FluentWindow` root).** Set `WindowBackdropType="None"` (was `"Mica"`) + `Background="#FF070707"`. (Nav/`ui:TitleBar` are `Background="Transparent"` → show black through; intended.)
- [ ] Build. Commit `refresh(theme): true-black base surface (Mica off, direct surface brushes)`.

### A2 — redirect raw background consumers
**Files:** `MainWindow.xaml` (empty-state ~line 560), `SectionDetailView.xaml` (~line 520), palette card hex (~line 637).
- [ ] `MainWindow.xaml` empty-state `{StaticResource ApplicationBackgroundBrush}` → `{StaticResource SurfaceBrush}`; `SectionDetailView.xaml` `{DynamicResource ApplicationBackgroundBrush}` → `{DynamicResource SurfaceBrush}`. The command-palette card `Background="#FF1E1E2E"` → `#FF101014`. **NOTE:** if Group B (palette removal) lands first in your stack, the palette-card hex is moot — skip it; otherwise change it here. (Stack order is A before B, so change it here; B then deletes the palette entirely. That's fine — A keeps the build valid.)
- [ ] Build. Commit `refresh(theme): redirect raw background consumers to true-black`.

### A3 — Ice Cyan accent
**Files:** `DesignTokens.xaml`, `src/VideoShelf.App/App.xaml.cs`.
- [ ] `<Color x:Key="AccentColor">#5CC8FF</Color>` → `#4FC3F7`.
- [ ] In `App.xaml.cs` startup, call `Wpf.Ui.Appearance.ApplicationAccentColorManager.Apply(System.Windows.Media.Color.FromRgb(0x4F,0xC3,0xF7));` so WPF-UI native controls match (confirm the exact API in WPF-UI 4.3.0; **STOP and report** if absent — then leave WPF-UI controls on OS accent + note it).
- [ ] Build. Commit `refresh(theme): Ice Cyan accent (#4FC3F7)`.

### Group A close-out
- [ ] Test gate green. **Full sweep** → TEXT verdict: true-black canvas everywhere (no Mica-gray leftovers), `#141414` cards read against black, brighter cyan accent, no washed/invisible text, no render failure, titlebar degrades cleanly with Mica off. Real-app `--view Browse`/`Home`/`SectionDetail` `OK` (the FluentWindow backdrop change is the risk).
- [ ] Push `feat/m24-a-black-refresh`, PR, CI, merge, sync.

---

## Group B — Cut Smart Views + Command Palette + Surprise Me — PR #2

> Remove three power-user surfaces end-to-end. **Read each feature's files first; grep for references before/after.**

### B1 — Remove Smart Views
- [ ] **Find all Smart-Views code:** `SmartViewsViewModel`, `SmartViewsView.xaml`, `SmartViewRepository`, `SmartViewSqlBuilder`, `SmartViewModels.cs` (`SmartRule`/`SmartViewDefinition`), `SmartRuleProse` (M23) + their tests; the `AppView.SmartViews` enum member, `MainViewModel.ShowSmartViewsCommand`/`SmartViews` property + DI, the Library-menu "Smart Views" `MenuItem` + content host + active-underline entry in `MainWindow.xaml`, the `'smart-views'` sweep `--view` + `HarnessRunner` case, and the **smart-view Home shelves** wiring in `DiscoveryViewModel` (the `showOnHome` smart-view rails). The M23 demo seeded a smart view in `HarnessRunner.SeedDemoAsync` — remove that seed line too.
- [ ] **STOP and report** if `SmartViewSqlBuilder` (or `parse_query`/scoped-run) is shared by a KEPT feature (search/recommendations). If it's only used by Smart Views, delete it; if shared, keep the shared core and remove only the Smart-Views VM/view/page/shelves.
- [ ] Delete the files, enum member, commands, DI, menu/host/underline, harness entries, seed line, and tests. Leave the `saved_searches`/`smart_collections` tables in the DB (no schema change) — just unused.
- [ ] Grep confirms zero dangling `SmartView*` references. Build. Commit `cut(smartviews): remove smart views (rule builder, page, home shelves)`.

### B2 — Remove the command palette
- [ ] **Find:** `CommandPaletteViewModel`, `PaletteItemViewModel`, `PaletteRanker`, the palette view/overlay in `MainWindow.xaml` (the `#FF1E1E2E`/now-`#FF101014` card + scrim ~lines 620–680), `OpenCommandPaletteCommand` + the `CommandPalette` property on `MainViewModel`, `MainViewModel.BuildActionRegistry()` (the whole action registry exists only for the palette — confirm and remove), the Ctrl+K key binding, the `'command-palette'` sweep `--view` + `HarnessRunner.NavigateCommandPalette`, and their tests.
- [ ] **STOP and report** if anything besides the palette consumes `PaletteRanker`/`BuildActionRegistry`. Delete end-to-end. Grep confirms no `Palette`/`CommandPalette` references remain. Build. Commit `cut(palette): remove Ctrl+K command palette`.

### B3 — Remove Surprise Me
- [ ] **Find:** the `AppView.SurpriseMe` member (if present) / `SurpriseMeCommand` on `MainViewModel`, any nav/menu entry, the random-pick logic, and tests. Delete. (It opens a random unwatched video — confirm no shared helper is used elsewhere.) Build. Commit `cut(discovery): remove Surprise Me`.

### Group B close-out
- [ ] Test gate (down from 1060 by the removed tests; `Failed: 0`). Sweep: Library menu no longer lists Smart Views; no Ctrl+K; Home has no smart-view shelves; no orphaned blank pages. Push `feat/m24-b-cut-smartviews-palette`, PR, CI, merge, sync.

---

## Group C — Cut player extras + accessibility remainder — PR #3

### C1 — Remove player extras
**Files:** `PlayerViewModel.cs`, `PlayerView.xaml`, the playback-engine seam (`IPlaybackEngine`/`LibVlcPlaybackEngine`), `Run-VisualSweep.ps1`/`HarnessRunner` player `--view` states.
- [ ] Remove these player features end-to-end (commands + bound UI in the "⋯ More"/tracks flyouts + engine members + tests + harness `--view`s):
  - **Aspect/zoom presets** (`AspectRatio`/`Scale`/the aspect-cycle command + the "⋯ More" aspect row; `player-aspect` sweep state).
  - **Volume normalization** (the `normvol` toggle + `SupportsVolumeNormalize` + `:audio-filter=normvol` at load + the tracks-flyout toggle).
  - **Frame-screenshot BUTTON** (the standalone "screenshot" command/button in "⋯ More" + `player-more` screenshot affordance) — **but KEEP the underlying `IThumbnailSnapshotter`/`TakeSnapshot` capability**, because Group F (creator portraits) and set-cover-from-frame need it. Only remove the user-facing screenshot button/command, not the snapshot service.
  - **Now-playing-in-titlebar** (`ComposeWindowTitle` now-playing composition — revert the window title to a static "VideoShelf").
  - **Speed?** NO — speed is KEPT. Do not remove speed.
  - **A-B repeat?** NO — KEPT. Do not remove.
- [ ] **STOP and report** if removing aspect/scale breaks the engine interface used by a kept path. Grep confirms removed commands have no XAML references. Build. Commit `cut(player): remove aspect/zoom, volume-normalize, screenshot button, titlebar now-playing`.

### C2 — Remove motion flourishes
**Files:** the M21 motion code.
- [ ] Remove: **series-complete celebration** (`SeriesCompleted` event + toast + animation), **PiP snap-to-corner animation** (keep PiP itself; remove only the snap `BeginAnimation` flourish — PiP still works, just no animated snap), **card→hero crossfade** (`HeroTransition`/the crossfade fallback). KEEP: reduced-motion gate, thumbnail fade, card hover, toasts+undo, skeleton loaders, basic view transitions, scroll memory.
- [ ] Build. Commit `cut(motion): remove celebration, PiP-snap animation, card→hero crossfade`.

### C3 — Remove the accessibility remainder (keep confirm/undo + tooltips)
**Files:** the M20 a11y code that survived PR #77.
- [ ] Remove: the **keyboard-navigation helpers** (`KeyboardNavigation.TabNavigation`/`DirectionalNavigation` setters added for a11y, `IsTabStop` a11y additions — leave default focusability, just remove the explicit a11y roving-tabindex attributes), **`TextSearch` type-ahead** wiring, the **`IFocusReturnService`** + its capture/restore calls in the OpenPlayer/ClosePlayer funnel, the **color-independent cues** (`Checkmark24` watched badge + `FractionToPercentText` "NN%" label added in M20 — keep the progress BAR, remove the %-text + ✓-badge that were the color-independent cue), and the **`HitTarget` 44px** style + its `BasedOn` usages (controls revert to their natural size).
- [ ] **KEEP:** `IConfirmService` confirm dialogs, all Undo (toast + buttons), and all `ToolTip`s. `AppFocusVisual` focus rings — **STOP and report** before removing: confirm whether they're purely a11y or also general mouse-UX; default to KEEP focus rings unless the owner-intent clearly cuts them (they're cheap and harmless). 
- [ ] Grep confirms no `IFocusReturnService`/`FractionToPercentText`/`HitTarget` references remain. Build. Commit `cut(a11y): remove keyboard-nav/focus-restore/type-ahead/color-cues/44px (keep confirm+undo+tooltips)`.

### Group C close-out
- [ ] Test gate. Sweep: player "⋯ More"/tracks flyouts no longer show aspect/normalize/screenshot; watched cards no longer show the ✓-badge/%-text (bar remains); PiP still works (no animated snap); confirm dialogs + undo toasts still fire. Real-app player launch `OK`. Push `feat/m24-c-cut-player-a11y`, PR, CI, merge, sync.

---

## Group D — Trim rename + grouping; rename Watchlist → Watch Later — PR #4

### D1 — Rename: keep only single-file rename
**Files:** the M5 `VideoShelf.Core.Renaming` (`CanonicalNamer`/`RenamePlanner`/`RenameExecutor`), `RenameToolViewModel`/`RenameToolView`, the M17 cross-series template rename (`MultiRenameViewModel`/`MultiRenameView`), `AppView.RenameTool`/`AppView.MultiRename`, the section-detail "Rename files…" entries, harness `--view` rename-tool/multi-rename states.
- [ ] **Remove the M17 cross-series template rename entirely** (`MultiRename*` VM/view, `AppView.MultiRename`, the "Rename files…" multi-series entry, harness `multi-rename` state, tests).
- [ ] **Reduce the M5 per-series rename to single-file rename:** the owner wants to rename ONE file at a time, not batch-canonicalize a whole series. Simplest safe path: **STOP and report your chosen approach before deleting** — either (a) keep `RenameToolViewModel` but restrict its UI/flow to a single selected episode (one source → one editable target → apply → undo), removing the whole-series batch planning UI; or (b) replace it with a small "Rename this file…" command on the episode row (inline editable name → `RenameExecutor` for one file → the existing crash-safe manifest + undo). **Preserve all M5 safety** (manifest-first, 2-arg `File.Move`, re-verify at apply, tolerant undo, `UpdateVideoPath` repaths `file_path`+`raw_filename`+`grouping_overrides.file_path`). The library-mutation discipline is non-negotiable [[user-preferences]].
- [ ] Build + tests (keep the single-file rename tests; remove batch/multi-rename tests). Commit `trim(rename): single-file rename only (remove batch + cross-series)`.

### D2 — Grouping: keep move-in/out + reorder; remove split/merge
**Files:** the M18 grouping-override code (`grouping_overrides` table usage, the split/merge/move/reorder UI on the creator page Edit mode, `SectionDetailViewModel` grouping commands), tests.
- [ ] **Keep:** move an episode from one series to another (`MoveEpisodeToSeriesCommand`/`override` write — the owner's "add/remove from a series" need) + manual episode **reorder** (`override_episode_no`). Ensure both are reachable in the creator-page Edit mode.
- [ ] **Remove:** the **split-series** and **merge-series** commands + their UI affordances + tests. **STOP and report** if split/merge share the same override write-path as move (so you don't break move) — if entangled, keep the shared write and remove only the split/merge UI + commands.
- [ ] Build + tests. Commit `trim(grouping): keep move-episode + reorder, remove split/merge`.

### D3 — Rename Watchlist → "Watch Later"
**Files:** `WatchlistViewModel`/`WatchlistView.xaml`, `AppView.Watchlist`, the nav/Library-menu label, the per-video watchlist toggle command + its button/tooltip text, any "watchlist" user-facing strings (NOT the DB column `in_watchlist`/`watchlist_at` — leave the schema; rename only the USER-FACING label + ideally the VM/view/command type names for clarity).
- [ ] Replace user-facing "Watchlist"/"Watch later" strings with **"Watch Later"** consistently (nav, page title, toggle button, tooltips, toast text). Renaming the C# type names (`WatchlistViewModel`→`WatchLaterViewModel`, `AppView.Watchlist`→`AppView.WatchLater`) is optional but preferred for consistency — if you rename types, update ALL references + DI + harness + tests. **Do NOT rename the DB columns** (`in_watchlist`/`watchlist_at`) — schema stays.
- [ ] Build + tests (update any test asserting the old label). Commit `rename(watch-later): Watchlist → "Watch Later" (UI + types; schema unchanged)`.

### Group D close-out
- [ ] Test gate. Sweep: only single-file rename remains; creator-page Edit mode shows move + reorder (no split/merge); nav/page reads "Watch Later". Push `feat/m24-d-trim-rename-grouping-rename`, PR, CI, merge, sync.

---

## Group E — Insights dashboard — PR #5

> Replace the tiny Home stats strip with a dedicated Insights page (offline, no-schema aggregates rendered as stat cards + Border-based bar charts — no charting dep). NOTE: Smart Views (B) is gone, but Insights does NOT depend on it.

### E1 — New stats queries (Core) — TDD
**Files:** `src/VideoShelf.Core/Models/StatsModels.cs`, `src/VideoShelf.Core/Storage/StatsRepository.cs`; tests in the stats-repo test file.
- [ ] **Read `StatsRepository`/`StatsModels`/`VideoShelfDb` schema first** (`videos.rating` REAL, `duration`, `watched`, `added_at`, `watch_events(video_id, watched_at)`, tag tables, `sections`). `$`-params, per-call `db.Open()`.
- [ ] Add (adapt to real casing): `RatingBucket(double Rating, int Count)` → `GetRatingDistribution()`; `WatchActivityPoint(string Period, int Count)` → `GetWatchActivityByMonth(int months)` (group `watch_events.watched_at` by `strftime('%Y-%m', …)`); `TagWatchStat(string Tag, int Total, int Watched)` → `GetTopTagsByWatch(int limit)` (join the canonical video-tag membership table); `LibraryComposition(int Creators, int Series, int Standalones, int TotalVideos, double TotalDurationSeconds)` → `GetLibraryComposition()`. Reuse existing `GetLibraryStats` + `GetTopCreatorsByWatched`.
- [ ] Tests cover each query on seeded data + empty-library (no throw). Commit `feat(core): insights stats queries`.

### E2 — Insights page
**Files:** new `InsightsViewModel.cs` + `InsightsView.xaml`; `MainViewModel` (`AppView.Insights` + `ShowInsightsCommand` + property), `MainWindow.xaml` (Library menu entry + content host + underline), DI, `Run-VisualSweep.ps1` + `HarnessRunner` (`Insights` `--view`).
- [ ] Mirror the `ShowHistory`/`ShowMaintenance` pattern. `Load()` calls the E1 queries; expose stat strings + bar collections (fractions via `FractionToWidth`); round all numbers. `InsightsView`: stat cards (total/watched/completion %/total hours) + watch-activity bar chart + ratings distribution + top creators + top tags, using `StatValue`/`Caption`/`TypeRailHeader`/`CardSurfaceBrush`/`AccentBrush`. Empty-library state.
- [ ] Build + sweep (`'insights'` `--view`). **STOP and report** if the harness can't reach the new view. Commit `feat(insights): dedicated insights dashboard`.

### Group E close-out
- [ ] Test gate; sweep verdict: Insights renders cards + bars on seeded data, clean empty state. Push `feat/m24-e-insights`, PR, CI, merge, sync.

---

## Group F — Creator portrait from a video frame (hybrid picker) — PR #6

> Offline: candidate-frame grid across the creator's videos **+** scrub-a-video to an exact frame. Saves PNG to `%LOCALAPPDATA%\VideoShelf\covers\`; writes only the `creator_art` path (library folders never written). No network.

### F1 — Arbitrary-position snapshot (service)
**Files:** `IThumbnailService.cs`/the `IThumbnailSnapshotter` interface, `LibVlcThumbnailService.cs`.
- [ ] **Read `LibVlcThumbnailService` + the interface.** Add `Task<bool> TrySnapshotAtAsync(string videoPath, string outputPngPath, TimeSpan position, CancellationToken ct)` — same headless flow but seek to clamped `position`. Refactor the existing default snapshot to call it. Fail-safe (never throw). NOTE: the standalone player screenshot BUTTON is removed in Group C, but this snapshot SERVICE stays (this feature needs it). Add a pure clamp/position helper + test. Commit `feat(service): arbitrary-position frame snapshot`.

### F2 — Candidate gathering + picker VM — TDD (pure parts)
**Files:** new `CreatorFramePickerViewModel.cs`; reuse `LibraryRepository.GetSeriesForSection`/`GetEpisodes`, `CreatorArtRepository.SetArtPath`, `AppPaths` covers dir.
- [ ] **Read `SectionDetailViewModel.SetCreatorArtCommand` + `CreatorArtRepository` + `AppPaths`.** Pure-tested helpers: `SelectCandidateVideos(videos, max)` (spread across series, skip missing, ≤max) + the saved-frame path builder under the covers dir. VM exposes `Candidates` (seed + lazy thumb), a `ScrubTarget` (video + `PositionSeconds` over its duration) + captured `Preview`, and `ConfirmCommand` (snapshot via `TrySnapshotAtAsync` → covers dir → `SetArtPath` → raise "done"). Commit `feat(creator-art): candidate gathering + frame-picker VM`.

### F3 — Picker UI + wire-in
**Files:** new `CreatorFramePickerView.xaml`; `SectionDetailView.xaml`/`SectionDetailViewModel.cs` entry; DI.
- [ ] Extend the existing creator-art entry to offer **"From a video frame…"** alongside "From a file…". The picker: a **candidate grid** + a **scrub panel** (pick a video → slider → "Capture frame" preview → "Use this frame"). Save → close → creator art refreshes. Frames land in the covers dir (never library folders). Build + sweep (add a picker `--view` if feasible; else verify-by-proxy + a real-app note; **STOP and report** if unreachable). Commit `feat(creator-art): hybrid frame picker (grid + scrub)`.

### Group F close-out
- [ ] Test gate; verdict: picker opens, shows candidates + scrub, setting a frame replaces the avatar. Push `feat/m24-f-creator-frames`, PR, CI, merge, sync.

---

## Group G — Reduced UX polish + ROADMAP flip — PR #7

> The original D1/D2 polish, **minus anything referencing cut features.** DROP: the Ctrl+K hint (palette cut), the Surprise-me button (cut), the SmartViews empty-state (cut). KEEP the rest.

### G tasks (one commit per change or batch closely-related)
- [ ] **Episode "⋯" overflow** — one `MoreHorizontal24` button on the episode row opening the existing favorite / Watch Later / add-to-playlist context menu (discoverable, still decluttered — reconciles #87). `SectionDetailView.xaml`.
- [ ] **Series tile "⋯" overflow** — a `MoreHorizontal24` button on the series tile header opening Play all / Play next / Add to queue / Mark (un)watched / **Rename this file** (the trimmed single-file rename) / move-episode. `SectionDetailView.xaml`.
- [ ] **"New playlist…" in the add-to-playlist flyout** when none exist. `SectionDetailView.xaml`/`EpisodeViewModel`.
- [ ] **Library-Health issue badge** when `MissingCount + DuplicateGroupCount > 0` (maintenance KEPT). `MainWindow.xaml` + a count on `MainViewModel`/`MaintenanceViewModel`. **STOP and report** if counts need a scan to compute.
- [ ] **Browse "Filter" label** beside the funnel toggle. `MainWindow.xaml`.
- [ ] **Distinct density-vs-list icons** (Compact density and List view-mode both use `List24` today). Give Compact a distinct glyph; keep `Grid24`/`Apps24`. `MainWindow.xaml`.
- [ ] **Select tooltips** on the multi-select "Select" toggles. `MainWindow.xaml`/`FavoritesView`/Watch-Later view/`SearchView`.
- [ ] **History empty-state copy** → "Videos you watch will appear here." `HistoryView.xaml`.
- [ ] **Queue empty state** → "Queue is empty — add videos from any creator page." `QueuePageView.xaml`.
- [ ] **Always-visible Back** (render disabled, not hidden, when `!CanGoBack`). `MainWindow.xaml`.
- [ ] **ROADMAP flip:** flip the M24 row to ✅ Merged (PR list #1–#7, final test count, one-line shipped summary) + a decision-log entry (durable gotchas: Mica-off true-black + accent-manager; the cut list + what was KEPT; offline frame-picker; #87-reconciling overflow; Watchlist→Watch-Later UI-only rename with schema unchanged; tables left orphaned by removals). Commit `docs(roadmap): M24 shipped`. Ride this branch.
- [ ] Push `feat/m24-g-polish-flip`, PR, CI green, merge `--merge --delete-branch`, sync.
- [ ] **Ping the owner** (Phase B handoff): M24 merged & CI-green.

---

## Acceptance criteria (whole milestone)
1. **Black-glass:** every view on true-black `#070707` + `#141414` cards + `#4FC3F7` accent; no Mica-gray leftovers, no render failure.
2. **Cuts gone, no orphans:** Smart Views, Ctrl+K palette, Surprise Me, the listed player extras, the a11y remainder, series split/merge, and batch/cross-series rename are removed end-to-end — no dead nav entries, no blank pages, no dangling references; the app builds, launches, and is green.
3. **Trims correct:** single-file rename works (with all M5 safety); creator-page Edit mode has move-episode + reorder (no split/merge); "Watch Later" replaces "Watchlist" everywhere user-facing (schema unchanged).
4. **Kept features intact:** Playlists, Watch Later, recommendation rails + tags, favorites + ratings, PiP, queue + up-next, A-B loop, speed, A–Z/breadcrumbs/scroll-memory, full maintenance suite — all still work.
5. **Insights:** a dedicated page with stat cards + bar charts on real data + empty state.
6. **Creator portraits:** set a creator image from a frame of their own videos (candidate grid + scrub), saved to the covers dir; no network; library untouched.
7. **Invariants:** confirm/undo + tooltips KEPT; no `AutomationProperties`/screen-reader; no `user_version`/schema change; no `ui:*` retemplate; library never written; suite green.

## STOP-and-report triggers (collected)
- A3: no `ApplicationAccentColorManager.Apply(Color)` in WPF-UI 4.3.0.
- B1: `SmartViewSqlBuilder`/`parse_query` shared by search/recommendations (don't break the kept feature).
- B2: anything besides the palette uses `PaletteRanker`/`BuildActionRegistry`.
- C1: removing aspect/scale breaks the engine interface a kept path uses.
- C3: `AppFocusVisual` focus rings ambiguous (a11y vs general UX) → default KEEP.
- D1: the single-file-rename reduction approach (restrict existing tool vs new per-file command) — confirm before deleting.
- D2: split/merge shares the move/reorder override write-path.
- E2 / F3: the harness can't reach the new Insights / frame-picker view.
- Any removal that leaves a dangling reference, blank page, or breaks a KEPT feature.

## Self-review (author)
- Every owner decision from the live review maps to a group: refresh→A, cut SmartViews/palette/SurpriseMe→B, cut player-extras/a11y→C, trim rename/grouping + rename Watchlist→D, insights→E, portraits→F, polish→G. ✓
- KEEP list explicitly fenced (Playlists, Watch Later, recommendations+tags, ratings/favorites, PiP, queue/up-next, A-B, speed, nav niceties, full maintenance, confirm/undo, tooltips). ✓
- Removals are end-to-end deletions with grep-for-dangling + STOP-on-entanglement guards; library-mutation safety preserved for single-file rename. ✓
- No schema change (orphaned tables left in place); offline frame picker keeps "no network for content". ✓
- New pure logic unit-tested; XAML/theme/removals verified by sweep + real-app launch. ✓
