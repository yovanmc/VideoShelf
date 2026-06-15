# M24 — Insights, Creator Portraits & a Black-Glass Refresh — Implementation Plan

> **Written for Sonnet execution.** This plan touches existing files digested by signature, not read line-by-line. For every task that EDITS an existing file: **read the current file first.** If its actual shape does not match what this plan describes, **STOP and report** rather than forcing the change.

**Goal:** Four owner-chosen prongs, shipped as ONE milestone via stacked PRs at the group seams (the M16/M23 model):
1. **Black-glass visual refresh** — move the base surface to true-black (`#070707`) and brighten the cyan accent to `#4FC3F7` ("Ice Cyan"). Owner picked this from a live palette exploration.
2. **Insights dashboard** — a dedicated page expanding the tiny Home stats strip into real library insights.
3. **Creator portraits from a video frame** — an OFFLINE hybrid picker (grid of auto-grabbed candidate frames across the creator's videos **+** scrub-a-video to an exact frame). This REPLACES the earlier "Google image lookup" idea — no network, no API key, no ToS/wrong-person risk; it keeps the app's **"no network for content"** constraint fully intact.
4. **Visibility & ease-of-use polish** — surface hidden features and clarify affordances (a concrete cut from a 24-item UX audit). **NOT accessibility** — the owner explicitly declined screen-reader/AutomationProperties work (PR #77 stands; keep UIA semantics OUT).

**Architecture:** 5 stacked PRs, split at group seams: **A** (palette refresh) → **B** (insights) → **C** (creator-frame picker) → **D1** (discoverability + nav) → **D2** (affordances + empty states + quick wins). The ROADMAP flip rides D2 (the final PR). App-layer + Core **read-path** + two additive features (creator-frame capture, insights queries) only.

**No `user_version` runner / no schema change** — every new capability reuses existing tables/columns (`videos.rating`/`duration`/`resume_position`/`added_at`, `watch_events`, `creator_art`, the tag tables). The streak M8→M23 holds. No `ui:*` control retemplate. Library files are never written (captured frames go to `%LOCALAPPDATA%\VideoShelf\covers\`).

**Tech stack:** .NET 10 · WPF + WPF-UI (`ui:FluentWindow`, Mica) · LibVLCSharp · `Microsoft.Data.Sqlite` · xUnit/Shouldly. `gh` is **not on PATH** → `& "C:\Program Files\GitHub CLI\gh.exe"`. Solution: `VideoShelf.slnx`.

---

## Conventions (apply to every task)

- **Test gate** (after every group, before every PR): `dotnet test VideoShelf.slnx -c Release --nologo -v q` → `Failed: 0`, total climbing from the **1060** baseline. Core flake → re-run the Core project alone to confirm; don't chase.
- **Build quietly:** `dotnet build VideoShelf.slnx -c Release -v minimal`.
- **Commits:** author `yovanmc`, trailer `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`. **No Codex trailer.** Merge `--merge` (no squash). `gh pr merge` from the main repo root, never a worktree. Direct pushes to `main` are blocked — every change ships via branch + PR. The ROADMAP flip rides D2.
- **CI:** after pushing each branch + opening its PR, sleep ~20s then `& "C:\Program Files\GitHub CLI\gh.exe" pr checks <PR#> --watch` in the foreground; merge only when green.
- **Theming (LOAD-BEARING — this codebase has had multiple WPF-UI regressions):** additive only. Never set `Style`/`Template` on a `ui:*` control for cosmetics. **Never alias a WPF-UI theme brush via a nested `<StaticResource ResourceKey=…>` and consume it as `{StaticResource}`** — it silently fails to materialise at merge time (the M15/M17 crash). Make new tokens DIRECT `<SolidColorBrush>` values. No `AutomationProperties`/screen-reader (PR #77 stands). [[wpfui-theming-and-visual-verification]]
- **Verification:** the `Run-VisualSweep.ps1` sweep (Debug build, ffmpeg fixtures) writes PNGs to `tests/screenshots/<stamp>/`; a **Sonnet subagent views them and returns a TEXT verdict** — never load PNGs into the controller. [[feedback-screenshot-verify-in-subagent]] Over-video/libVLC content is GDI-uncapturable (verify-by-proxy). After any XAML/window restructure, the sweep's real-app `--view <X> --done-signal` launches double as the render-crash backstop (XAML/template crashes pass build+unit tests — cf. the M22 `ScrollMemory.ViewKey` crash). **Group A note:** the GDI sweep can FALSE-FAIL small/dense UI at full-window zoom — if a verdict hinges on small regions, crop+upscale before judging (the M23 Group C lesson).

---

## Group A — Black-glass refresh (Ice Cyan on true-black) — PR #1

> Owner picked "Ice Cyan `#4FC3F7` on true-black `#070707`" from a live palette exploration. The base today is the WPF-UI **Mica** backdrop (~`#1C1C1C`); `SurfaceBrush`/`CardSurfaceBrush` are **aliases** to WPF-UI keys, so a color edit alone won't work — Mica must be turned off and the surface tokens made direct.

### Task A1: Make the base surface true-black
**Files:** `src/VideoShelf.App/Resources/DesignTokens.xaml`, `src/VideoShelf.App/Views/MainWindow.xaml`.
- [ ] **Read `DesignTokens.xaml` first.** Convert the two aliases to DIRECT brushes (this also fixes the latent nested-`<StaticResource>` fragility):
  - `SurfaceBrush`: replace `<StaticResource ResourceKey="ApplicationBackgroundBrush" />` → `<SolidColorBrush x:Key="SurfaceBrush" Color="#FF070707" />`
  - `CardSurfaceBrush`: replace `<StaticResource ResourceKey="ControlFillColorDefaultBrush" />` → `<SolidColorBrush x:Key="CardSurfaceBrush" Color="#FF141414" />` (a subtle elevation so cards read against the true-black canvas).
  - Leave `TextPrimary/Secondary/Muted`, `DividerBrush`, `ChipFillBrush`, the player scrims (already near-black) unchanged.
- [ ] **Read `MainWindow.xaml` (the `ui:FluentWindow` root).** Turn off Mica so the solid black canvas shows: set `WindowBackdropType="None"` (it's currently `"Mica"`) and add `Background="#FF070707"` on the `ui:FluentWindow` (or its root Grid). The nav bar / `ui:TitleBar` are `Background="Transparent"` (M87) → they'll show the solid black through, which is intended.
- [ ] Build. Commit `refresh(theme): true-black base surface (Mica off, direct surface brushes)`.

### Task A2: Redirect the raw `ApplicationBackgroundBrush` consumers
**Files:** `MainWindow.xaml` (empty-state overlay ~line 560), `SectionDetailView.xaml` (~line 520), and the command-palette card hex (~line 637).
- [ ] Two views consume the WPF-UI key directly and will NOT pick up the token change: `MainWindow.xaml` empty-state `Background="{StaticResource ApplicationBackgroundBrush}"` → `{StaticResource SurfaceBrush}`; `SectionDetailView.xaml` `Background="{DynamicResource ApplicationBackgroundBrush}"` → `{DynamicResource SurfaceBrush}`.
- [ ] The command-palette card is a hardcoded `Background="#FF1E1E2E"` (~MainWindow line 637) → change to `#FF101014` (near-black with a hair of the cyan-cool tint). Leave the palette scrim `#99000000` and divider `#22FFFFFF` as-is.
- [ ] Build. Commit `refresh(theme): redirect raw background consumers to true-black surface`.

### Task A3: Brighten the accent everywhere (token + WPF-UI controls)
**Files:** `DesignTokens.xaml`, `src/VideoShelf.App/App.xaml.cs`.
- [ ] In `DesignTokens.xaml`: `<Color x:Key="AccentColor">#5CC8FF</Color>` → `#4FC3F7`. This recolors every element that uses `{StaticResource AccentBrush}` (nav underlines, focus ring, eyebrows, chip-toggle checked, seek/progress fills, ratings, etc.).
- [ ] **WPF-UI native controls** (primary buttons, slider thumb, checkbox ticks) follow the OS accent, NOT our token — there is no `ApplicationAccentColorManager` call today. To make them match Ice Cyan: in `App.xaml.cs` startup (after the host/DI is built, before/at `OnStartup`), call `Wpf.Ui.Appearance.ApplicationAccentColorManager.Apply(System.Windows.Media.Color.FromRgb(0x4F, 0xC3, 0xF7));` (confirm the exact namespace/type against the WPF-UI 4.3.0 DLL — it may be `ApplicationAccentColorManager.Apply(Color)` or `ApplicationThemeManager`/`AccentColorManager`; **STOP and report** if no such API exists and instead leave WPF-UI controls on OS accent + note it). Keep it a single startup call; do not retemplate controls.
- [ ] Build. Commit `refresh(theme): Ice Cyan accent (#4FC3F7) token + WPF-UI accent`.

### Group A close-out
- [ ] Test gate green. **Full sweep** via a Sonnet subagent → TEXT verdict across ALL views: true-black canvas everywhere (no leftover `#1C1C1C` Mica-gray panels), cards/panels read against black, cyan accent is the brighter `#4FC3F7`, no washed-out or invisible text, no white/black render failure, titlebar degrades cleanly with Mica off. Pay special attention to: Home, Browse, creator page, Player flyouts, Maintenance, DuplicateResolve, Settings, command palette. Real-app `--view Browse`/`Home`/`SectionDetail` launches `OK` (render-crash backstop — the FluentWindow backdrop change is the risk).
- [ ] Push `feat/m24-a-black-refresh`, PR, CI green, merge, sync.

---

## Group B — Insights dashboard — PR #2

> Today: `StatsRepository.GetLibraryStats()` + `GetTopCreatorsByWatched(limit)` feed a 2-line text strip on Home. Build a dedicated **Insights** page with richer (offline, no-schema) aggregates rendered as stat cards + simple Border-based bar charts (the app has no charting dep — reuse the track+fill `Border` pattern).

### Task B1: New stats queries (Core) — TDD
**Files:** `src/VideoShelf.Core/Models/StatsModels.cs` (add records), `src/VideoShelf.Core/Storage/StatsRepository.cs` (add methods); tests in the existing stats-repo test file.
- [ ] **Read `StatsRepository` + `StatsModels` + `VideoShelfDb` schema first** to confirm column/table names (`videos.rating` REAL, `videos.duration`, `videos.watched`, `videos.added_at`, `watch_events(video_id, watched_at)`, the tag tables, `sections`). Use `$`-params and per-call `db.Open()` (match the existing methods).
- [ ] Add these records + methods (adapt names to the real column casing you find):
  - `RatingBucket(double Rating, int Count)` → `GetRatingDistribution()` — `SELECT rating, COUNT(*) FROM videos WHERE missing=0 AND rating>0 GROUP BY rating ORDER BY rating`.
  - `WatchActivityPoint(string Period, int Count)` → `GetWatchActivityByMonth(int months)` — group `watch_events.watched_at` by `strftime('%Y-%m', watched_at)`, last N months, ordered ascending. (Bars over time.)
  - `TagWatchStat(string Tag, int Total, int Watched)` → `GetTopTagsByWatch(int limit)` — join `video_tags`→`videos` (and/or section/series tags — pick the tag table that actually drives video membership; confirm which the Smart-view builder treats as the canonical tag). Group by tag, order by Total desc.
  - `LibraryComposition(int Creators, int Series, int Standalones, int TotalVideos, double TotalDurationSeconds)` → `GetLibraryComposition()` — counts from `sections`/`series`/`videos`.
  - (Reuse the existing `GetLibraryStats` for the headline completion numbers; add `int UnwatchedVideos => Total - Watched - InProgress` in the VM, not a new query.)
- [ ] Tests: seed via the existing stats-test fixture (or `StressLibrarySeeder`), assert each query returns expected shapes/counts on known data; assert empty-library returns empty lists / zeroes without throwing.
- [ ] Commit `feat(core): insights stats queries (ratings/activity/tags/composition)`.

### Task B2: Insights page (VM + view + nav)
**Files:** new `src/VideoShelf.App/ViewModels/InsightsViewModel.cs`, new `src/VideoShelf.App/Views/InsightsView.xaml`; `MainViewModel.cs` (`AppView` enum + `ShowInsightsCommand` + property), `MainWindow.xaml` (Library menu entry + content host + nav underline), `ServiceCollectionExtensions.cs` (DI), optionally `BuildActionRegistry()` (palette entry).
- [ ] **Read `MainViewModel` (`AppView` enum ~line 15, the `ShowHistory`/`ShowMaintenance` command pattern, `PushNav`) + `MainWindow.xaml` (the Library `Menu` block ~lines 107–115 and the `EnumToVis`/`EnumSetToVis` content hosts).** Add `Insights` to `AppView`; add `InsightsViewModel Insights { get; }` + `[RelayCommand] void ShowInsights()` (calls `Insights.Load(); PushNav(CurrentView); CurrentView = AppView.Insights;`). Register VM in DI.
- [ ] `InsightsViewModel.Load()` calls the B1 queries + `GetLibraryStats`/`GetTopCreatorsByWatched`; expose stat strings + `ObservableCollection`s for the bar lists. Compute bar widths as fractions (reuse `FractionToWidth`). Round all displayed numbers.
- [ ] `InsightsView.xaml`: a scrollable page — a row of stat cards (total / watched / completion % / total hours), a "watch activity" bar chart (months), a "ratings" distribution bar row, "top creators" + "top tags" lists. Use the design tokens (`StatValue`, `Caption`, `TypeRailHeader`, `CardSurfaceBrush`, `AccentBrush` for bar fills). No new colors.
- [ ] Wire a Library-menu `MenuItem` "Insights" → `ShowInsightsCommand`; add the content host (`EnumToVis ConverterParameter=Insights`); add `Insights` to the Library tab's active-underline set. Optionally add "Insights" to `BuildActionRegistry()`.
- [ ] Build + sweep (add an `'insights' = @('--view','Insights','--seed-demo')` entry to `Run-VisualSweep.ps1` and an `AppView.Insights` case in `HarnessRunner` mirroring how Maintenance is driven — **STOP and report** if the harness can't reach the new view). Commit `feat(insights): dedicated insights dashboard page`.

### Group B close-out
- [ ] Test gate; sweep verdict: Insights renders stat cards + bar charts with seeded data, no breakage. Push `feat/m24-b-insights`, PR, CI, merge, sync.

---

## Group C — Creator portrait from a video frame (hybrid picker) — PR #3

> Owner idea (replaces Google lookup): set a creator's image from a frame of THEIR OWN videos. **Hybrid:** a grid of auto-grabbed candidates (one per video across the creator's library) for a fast pick **+** scrub a chosen video to an exact frame. Offline; saves the PNG to `%LOCALAPPDATA%\VideoShelf\covers\`; writes only the `creator_art` path (never library folders).

### Task C1: Arbitrary-position snapshot (service) — TDD where possible
**Files:** `src/VideoShelf.App/Services/IThumbnailService.cs` (or wherever `IThumbnailSnapshotter` is declared), `src/VideoShelf.App/Services/LibVlcThumbnailService.cs`.
- [ ] **Read `LibVlcThumbnailService` + the `IThumbnailSnapshotter` interface.** Today `TrySnapshotAsync(videoPath, outputPngPath, ct)` seeks to a hardcoded `Min(Length/10, 3000ms)` then `TakeSnapshot`. Add an overload that accepts a position:
  `Task<bool> TrySnapshotAtAsync(string videoPath, string outputPngPath, TimeSpan position, CancellationToken ct)` — same headless `LibVLC`/`MediaPlayer` flow, but seek to `position` (clamp to `[0, Length]`), delay to let the frame settle, `TakeSnapshot`. Refactor the existing `TrySnapshotAsync` to call `TrySnapshotAtAsync(..., default-position, ct)` so there's one implementation. Must remain **fail-safe (never throw)** — return `false` on any error.
- [ ] Add a tiny pure helper + test for the position clamp / candidate-position selection (the libVLC snapshot itself is integration — verified at the app via the picker, not unit-tested). Commit `feat(service): arbitrary-position frame snapshot`.

### Task C2: Candidate gathering + picker VM — TDD (pure parts)
**Files:** new `src/VideoShelf.App/ViewModels/CreatorFramePickerViewModel.cs`; reuse `LibraryRepository.GetSeriesForSection(sectionId)` + `GetEpisodes(seriesId)`, `CreatorArtRepository.SetArtPath`, `AppPaths.CoversDirectory`.
- [ ] **Read `SectionDetailViewModel.SetCreatorArtCommand` (~line 431) + `CreatorArtRepository` + `AppPaths`** (confirm `CoversDirectory`). Pure-testable logic to extract + test: given the creator's videos, choose up to N (e.g. 9) **candidate seed paths** (spread across series, skip missing files) — a pure `SelectCandidateVideos(IReadOnlyList<...>, int max)` helper with a test (deterministic spread, missing excluded, ≤max). And the saved-frame path builder (`covers/creator-{sectionId}-{timestamp-or-guid}.png` under `CoversDirectory`) — pure + tested.
- [ ] The VM exposes: `Candidates` (each = seed video + a lazily-snapshotted thumbnail path), a `ScrubTarget` (a chosen video + a `PositionSeconds` slider bound to its duration), a captured-frame `Preview`, and `ConfirmCommand` (saves the chosen PNG via `TrySnapshotAtAsync` → `CoversDirectory` → `CreatorArtRepository.SetArtPath(sectionId, path)` → raise a "done" event so the creator page refreshes its art). All snapshot calls are async/fire-safe.
- [ ] Commit `feat(creator-art): candidate gathering + frame-picker view-model`.

### Task C3: Picker UI + wire into the creator page
**Files:** new `src/VideoShelf.App/Views/CreatorFramePickerView.xaml` (a dialog/overlay), `SectionDetailView.xaml` + `SectionDetailViewModel.cs` (entry point), DI.
- [ ] The existing `SetCreatorArtCommand` opens a file picker (`IImagePicker.PickImage`). Extend the entry point so the creator-art affordance offers **"From a video frame…"** (opens the new picker) alongside the existing **"From a file…"**. (Either a small menu/flyout on the art button, or two buttons — match the creator-page hero's existing style.)
- [ ] `CreatorFramePickerView`: a **candidate grid** (tap a frame → confirm) + a **scrub panel** (pick a video from the creator, a slider over its duration, a "Capture frame" button showing a live preview, then "Use this frame"). Show a neutral state while frames generate. Save → close → creator art refreshes (the avatar fallback is replaced by the chosen frame).
- [ ] On confirm, the saved frame must land in `CoversDirectory` (never a library folder) and update `creator_art`. Build + sweep (add a `'creator-frame-picker'` harness `--view` state if feasible, driving the picker open for a seeded creator; **STOP and report** if the harness can't open it — then verify-by-proxy with unit tests + a real-app manual note).
- [ ] Commit `feat(creator-art): hybrid frame picker (candidate grid + scrub)`.

### Group C close-out
- [ ] Test gate; sweep/real-app verdict: the picker opens, shows candidate frames + a scrub panel, and setting a frame replaces the creator's avatar with the chosen image. Push `feat/m24-c-creator-frames`, PR, CI, merge, sync.

---

## Group D1 — Discoverability + navigation — PR #4

> Surface features that are currently right-click-only / buried, WITHOUT undoing the #87 episode-row declutter. The reconciliation: a SINGLE "⋯" overflow button per row/tile (not 5 inline icons) — visible + discoverable, still uncluttered.

### Task D1 tasks (one commit per logical change, or batch 2-3 closely-related)
- [ ] **Ctrl+K hint:** add a subtle `"Ctrl K"` badge as a suffix inside/next to the nav search box so the palette is discoverable — `MainWindow.xaml`. (Static chip using `ChipFillBrush`/`TextMutedBrush`.)
- [ ] **Surprise me → visible nav button:** add a dice button (`ui:SymbolIcon`, a valid WPF-UI symbol e.g. `Dice24` — verify; else `Sparkle24`) in the nav bar bound to the existing surprise-me command (find it on `MainViewModel`) with `ToolTip="Surprise me"` — `MainWindow.xaml`.
- [ ] **Scan affordance in nav/Library:** add a visible "Rescan" entry (Library menu item and/or a nav button) bound to `ScanAndReloadCommand` with a tooltip — `MainWindow.xaml`. (Today scan is Settings-only.)
- [ ] **Series tile "⋯" overflow:** the series tile exposes only `ActivateCommand`; its Play all / Play next / Add to queue / Mark (un)watched / Rename actions are right-click-only. Add a `ui:Button` (`MoreHorizontal24`) docked top-right of the series tile header (visible, or visible-on-hover) opening a flyout/`ContextMenu` with those existing commands — `SectionDetailView.xaml`. Reuse the commands already on the series VM (no new commands).
- [ ] **Episode "⋯" overflow (reconciles #87):** add ONE `MoreHorizontal24` button to the episode row's compact line 2 that opens the SAME context menu currently bound to the row's right-click (`ToggleFavorite`/`Watch later`/`Add to playlist`). Keeps the row decluttered (one button, not five) while making the actions discoverable — `SectionDetailView.xaml`.
- [ ] **"New playlist…" in the add-to-playlist flyout:** the `AvailablePlaylists` submenu is empty when no playlists exist. Append a "New playlist…" `MenuItem` (bound to a create-then-add command — find/extend the playlist create command) — `SectionDetailView.xaml` / `EpisodeViewModel`.
- [ ] **Library Health issue badge:** when `MissingCount + DuplicateGroupCount > 0`, show a small count badge on the "Library Health" Library-menu item — `MainWindow.xaml` + a count exposed on `MainViewModel`/`MaintenanceViewModel` (reuse existing maintenance counts; **STOP and report** if they aren't cheaply available without a scan).
- [ ] Build + sweep (Browse + creator page + an open Library menu if capturable). Commit per change. Push `feat/m24-d1-discoverability`, PR, CI, merge, sync.

---

## Group D2 — Affordances, empty states & quick wins (+ ROADMAP flip) — PR #5

### Task D2 tasks
- [ ] **Filter toggle label:** the Browse filter `ChipToggle` is funnel-icon-only; add a "Filter" text label beside the icon — `MainWindow.xaml`.
- [ ] **Distinct density icons:** Compact density and List view-mode both use `List24` (ambiguous). Give Compact a distinct glyph (e.g. `TextAlignJustified24`/`LineHorizontal324` — verify exists), keep `Grid24` (Normal) / `Apps24` (Spacious), and keep List-view-mode's `List24` — so density vs view-mode read differently — `MainWindow.xaml`.
- [ ] **Select tooltips:** add `ToolTip="Select multiple to bulk-edit"` to the "Select" `ChipToggle` on Browse / Favorites / Watchlist / Search — `MainWindow.xaml`, `FavoritesView.xaml`, `WatchlistView.xaml`, `SearchView.xaml`.
- [ ] **SmartViews empty state + examples hint:** when `Views.Count == 0`, show an empty-state TextBlock in the left column ("Create your first smart view with the builder →"); add a one-line examples hint under the builder ("e.g. tag is anime · unwatched · rating ≥ 4") — `SmartViewsView.xaml`.
- [ ] **History empty-state copy:** change "episodes you finish will appear here" → "Videos you watch will appear here." — `HistoryView.xaml`.
- [ ] **Queue empty state:** add a "Queue is empty — add videos from any creator page" TextBlock to `QueuePageView.xaml` (shown when the queue is empty).
- [ ] **Always-visible Back:** render the Back button disabled (not hidden) when `!CanGoBack` so the affordance is consistent — `MainWindow.xaml`. (Confirm the current binding hides it; switch `Visibility` → `IsEnabled`.)
- [ ] Build + sweep across Browse / SmartViews / History / Queue / Favorites. Commit per change.
- [ ] **ROADMAP flip:** flip the M24 row to ✅ Merged (PR list #1–#5 actual numbers, final test count, one-line shipped summary) + a decision-log entry (durable gotchas: Mica-off true-black approach, the accent-manager call, the offline frame-picker replacing Google, the #87-reconciling overflow pattern, any harness `--view` additions). Commit `docs(roadmap): M24 shipped`, ride this branch.
- [ ] Push `feat/m24-d2-polish-flip`, PR, CI green, merge `--merge --delete-branch`, sync.
- [ ] **Ping the owner** (Phase B handoff): M24 merged & CI-green.

---

## Acceptance criteria (whole milestone)
1. **Black-glass:** every view renders on a true-black (`#070707`) canvas with `#141414` cards and the brighter `#4FC3F7` accent; no leftover Mica-gray panels, no washed-out/invisible text, no render failure — verified on a full sweep.
2. **Insights:** a dedicated Insights page (reachable from nav) shows stat cards + bar charts (watch activity, ratings, top creators/tags, composition) on real data, with a clean empty-library state.
3. **Creator portraits:** a creator's image can be set from a frame of their own videos — a candidate grid AND a scrub-to-exact-frame mode; the chosen PNG saves under `%LOCALAPPDATA%\VideoShelf\covers\` and replaces the avatar; no network used; library folders untouched.
4. **Visibility & ease:** Ctrl+K is hinted; Surprise-me and Rescan are visible in nav; series + episode hidden actions are reachable via a single "⋯" overflow (declutter preserved); Filter is labeled; density vs list-mode icons are distinct; Select has tooltips; SmartViews/Queue have empty states; Back is consistently present.
5. **Invariants:** no `user_version` runner / no schema change; no `ui:*` retemplate; no `AutomationProperties`/screen-reader; library never written; full suite green (≥ 1060 + new tests).

## STOP-and-report triggers (collected)
- A3: no `ApplicationAccentColorManager.Apply(Color)` (or equivalent) API in WPF-UI 4.3.0 → leave WPF-UI controls on OS accent and note it.
- B2 / C3 / D1: the harness can't drive the new `--view` (Insights / creator-frame-picker) or reach a populated state → verify-by-proxy (unit tests + a real-app manual note) and report.
- C: `AppPaths.CoversDirectory` doesn't exist (use the actual covers/thumbs dir name you find).
- D1: maintenance counts (missing/duplicate) aren't available without triggering a scan → skip the Library Health badge and report.
- Any palette change that leaves a view unreadable or a control invisible against true-black.

## Self-review (author)
- Every owner ask maps to a group: palette→A, insights→B, image lookup (reframed offline)→C, UX polish→D1/D2. ✓
- True-black approach grounded in the real theming wiring (Mica off + direct surface brushes + accent manager), with the nested-`<StaticResource>` trap avoided. ✓
- Image lookup is OFFLINE — no network, no API key, constraint intact; builds on existing `creator_art` + libVLC snapshot. ✓
- Additive only; no migration (reuses existing tables/columns). ✓
- #87 declutter reconciled via a single "⋯" overflow, not re-added inline icons. ✓
- New pure logic (stats queries, candidate selection, path building, position clamp) is unit-tested; XAML/theme/wiring verified by sweep + real-app launch. ✓
- Accessibility explicitly excluded per owner. ✓
