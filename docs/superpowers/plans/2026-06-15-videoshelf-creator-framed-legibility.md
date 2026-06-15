# M23 — Creator-framed Legibility & Findability — Implementation Plan

> **Written for Sonnet execution.** This plan touches existing files digested by signature, not read line-by-line. For every task that EDITS an existing file: **read the current file first.** If its actual shape does not match what this plan describes, **STOP and report** rather than forcing the change.

**Goal:** Act on the 4-persona UX audit (see the M23 ROADMAP row + the "M23 scoped" decision-log entry), framed around the owner's decision that **every source folder is a person → lean into "Creator"**. Make a real library *legible* (cards show art or a creator avatar, episode rows show enough to choose) and *findable* (search + palette reach series/episodes), plus a polish/onboarding pass.

**Architecture:** Seven stacked PRs, one per group (A→G), the M16–M22 model. App-layer + a few Core **read-path** changes only. **No `user_version` runner** (additive: `EpisodeView` gains optional fields, no schema change — `videos.duration`/`resume_position` already exist; `Rating` is already `double`). No `ui:*` control retemplate; the only `DesignTokens` change is adding one muted-text brush + using it (no palette recolor). Library files never written.

**Tech stack:** .NET 10 · WPF + WPF-UI · LibVLCSharp · `Microsoft.Data.Sqlite` · xUnit/Shouldly. `gh` is **not on PATH** → `& "C:\Program Files\GitHub CLI\gh.exe"`. Solution: `VideoShelf.slnx`.

---

## ⚠️ Audit calibration (read before starting — several findings were screenshot misreads)

The audit personas judged from static screenshots of a demo fixture. The code digest corrected these — **do NOT "fix" non-problems**:

- **Search already returns videos.** `SearchViewModel` already calls `SearchCreators` + `SearchVideos` and shows both (`CreatorResults`, `VideoResults`). The real gaps (Group B): **no series-level results**, **no result-count summary**, **no section headers/grouping labels**, and direct-play exists but only via the video-card selection path. Do NOT rebuild search; extend it.
- **The command palette already has content + actions.** `CommandPaletteViewModel.RunAsync` already calls `SearchCreators`+`SearchVideos` and `MainViewModel.BuildActionRegistry()` already has 11 entries (Home/Browse/Settings/SmartViews/Playlists/Watchlist/Favorites/History/Queue/Surprise Me/Scan Library). The real gaps (Group D): **no series results**, **no Maintenance / New-Smart-View / Add-Source action entries**. Do NOT rebuild the palette; add to it.
- **Browse toolbar buttons already have tooltips** (Filter/Compact/Normal/Spacious/Grid/List). The real gap (Group F): the density glyphs (`TextGrammarArrowLeft24`/`TextGrammarArrowRight24`) are *ambiguous at a glance*. Improve the glyphs/affordance; tooltips already exist.
- **`TextSecondaryBrush` is `#C5FFFFFF` (≈77% white) — already high-contrast.** The dim metadata text is from ad-hoc `Opacity="0.6"`/`"0.7"` on labels (the `Caption` style, History date, smart-view summary). Group F fixes *those*, not the token.
- **The `browse-selection` "(" glyph is a plain WPF `CheckBox`** (no `ui:SymbolIcon` in any selection path). Treat Group F's glyph item as **verify-live-then-fix-if-real**, not a known code fix.

---

## Conventions (apply to every task)

- **Test gate** (after every group, before every PR): `dotnet test VideoShelf.slnx -c Release --nologo -v q` → `Failed: 0`, total climbing from the **970** baseline (378 Core + 592 App). The Core parallel-flake was fixed in #90; if a Core test still flakes in a full run, re-run the Core project alone to confirm — don't chase it.
- **Build quietly:** `dotnet build VideoShelf.slnx -c Release -v minimal`.
- **Worktrees:** `.worktrees/m23-<group>`; **`gh pr merge` from the main repo root**, never the worktree. Direct pushes to `main` are blocked — every change ships via branch + PR.
- **Commits:** author `yovanmc`, trailer `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`. **No Codex trailer.** Merge `--merge` (no squash). The ROADMAP flip rides Group G.
- **CI:** after pushing each branch + opening its PR, sleep ~20s then `& "C:\Program Files\GitHub CLI\gh.exe" pr checks <PR#> --watch` in the foreground; merge only when green.
- **Theming (load-bearing):** additive only — never retemplate a `ui:*` control; no palette recolor (the one allowed token change is adding a muted-text brush). No `AutomationProperties`/screen-reader (PR #77 removed it; it stays out). Library files never written. [[wpfui-theming-and-visual-verification]]
- **Verification:** the `Run-VisualSweep.ps1` sweep (Debug build, ffmpeg fixtures) writes PNGs to `tests/screenshots/<stamp>/`; a **Sonnet subagent views them and returns a TEXT verdict** — never load PNGs into the controller. [[feedback-screenshot-verify-in-subagent]] Over-video/libVLC content is GDI-uncapturable (verify-by-proxy). After any XAML restructure, do a real-app `--view <X> --done-signal` launch (render-crash backstop — XAML template crashes pass build+unit tests; cf. the M22 `ScrollMemory.ViewKey` crash).

---

## File-structure map

**New files**
- `src/VideoShelf.App/Visuals/CreatorAvatar.cs` — pure: name → initials + deterministic hue.
- `src/VideoShelf.App/Visuals/CreatorAvatarConverters.cs` — `StringToInitialsConverter`, `StringToAvatarBrushConverter` (thin, wrap CreatorAvatar).
- `src/VideoShelf.Core/Discovery/SmartRuleProse.cs` — pure: `SmartViewDefinition` (+ optional creator-name map) → plain-English summary.
- Tests: `tests/VideoShelf.App.Tests/Visuals/CreatorAvatarTests.cs`, `tests/VideoShelf.Core.Tests/Discovery/SmartRuleProseTests.cs`, plus targeted VM/repo tests noted per task.

**Modified (high level)**
- A: `CreatorCard.xaml`, `CreatorCardViewModel.cs` (Initials/AvatarBrush), `VideoCard.xaml`, `RecencyCardViewModel.cs`, `ContinueWatchingCardViewModel.cs` (+ thumbnail loading), DI (`ServiceCollectionExtensions.cs`).
- B: `LibraryRepository.cs` (+`SearchSeries`), `SearchViewModel.cs`, `SearchView.xaml`, a `SeriesResultViewModel`.
- C: `LibraryRepository.cs` (`GetEpisodes`/`GetEpisode` join duration+resume), `BrowseModels.cs` (`EpisodeView` optional fields), `EpisodeViewModel.cs`, `SectionDetailView.xaml` (episode row).
- D: `MainViewModel.cs` (`BuildActionRegistry`), `CommandPaletteViewModel.cs` (series results).
- E: `SmartViewsViewModel.cs` (use `SmartRuleProse`), `SmartViewRepository.cs` (+count), builder match-count wiring.
- F: `DesignTokens.xaml` (+`TextMutedBrush`), label sites (Caption/History/smart-view), Browse density glyphs in `MainWindow.xaml`, `HistoryViewModel.cs`/`HistoryView.xaml`, `PlaylistsViewModel.cs`/`PlaylistsView.xaml`, (verify) selection checkbox.
- G: `MainWindow.xaml` (empty-state copy + persistent Add-folder), `StressLibrarySpec.cs` (person names) + its tests, `tools/harness/Run-VisualSweep.ps1` if a new `--view` state helps, `ROADMAP.md` (flip — Group G only).

---

## Group A — Creator-centric card art + avatar fallback — PR #1

> Owner decision: no-art creator cards show a **circular profile avatar** (initials + per-creator hue). Video cards get **real thumbnails populated** (today `Cover => null`), with a neutral fallback.

### Task A1: Pure `CreatorAvatar` helper (initials + deterministic hue) — TDD
**Files:** create `src/VideoShelf.App/Visuals/CreatorAvatar.cs`; test `tests/VideoShelf.App.Tests/Visuals/CreatorAvatarTests.cs`.

- [ ] **Failing test** — assert: `Initials("Alice Autumn") == "AA"`; `Initials("Madonna") == "M"`; `Initials("  bruno  bay ") == "BB"` (trim, upper, max 2); `Initials("") == "?"`. And determinism: `HueDegrees("Alice A")` is in `[0,360)` and equal across calls; two different names *usually* differ (assert at least that the same name is stable, and that `HueDegrees` is pure).
- [ ] **Implement:**
```csharp
namespace VideoShelf.App.Visuals;

/// <summary>Pure helpers for a creator's fallback avatar: initials + a deterministic hue
/// derived from the name (so the same creator always gets the same color). No WPF deps here.</summary>
public static class CreatorAvatar
{
    public static string Initials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";
        var words = name.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return "?";
        var first = char.ToUpperInvariant(words[0][0]);
        if (words.Length == 1) return first.ToString();
        var last = char.ToUpperInvariant(words[^1][0]);
        return $"{first}{last}";
    }

    /// <summary>Stable hue 0..359 from the name. Uses a stable hash (NOT string.GetHashCode,
    /// which is randomized per-process) so colors are consistent across runs.</summary>
    public static int HueDegrees(string? name)
    {
        if (string.IsNullOrEmpty(name)) return 0;
        unchecked
        {
            uint h = 2166136261u;                 // FNV-1a
            foreach (char c in name) { h ^= c; h *= 16777619u; }
            return (int)(h % 360u);
        }
    }
}
```
> **NOTE:** do not use `string.GetHashCode()` (per-process randomized → unstable colors). The FNV-1a above is intentional.
- [ ] Test passes. Commit `feat(a11y-visual): pure creator-avatar initials + deterministic hue`.

### Task A2: Avatar converters + show the avatar on creator cards with no art
**Files:** create `src/VideoShelf.App/Visuals/CreatorAvatarConverters.cs`; edit `CreatorCard.xaml`; expose `Name` (already present as `CreatorCardViewModel.Name`).

- [ ] `StringToInitialsConverter` → `CreatorAvatar.Initials(value as string)`. `StringToAvatarBrushConverter` → a `SolidColorBrush` from `CreatorAvatar.HueDegrees(name)` via HSL→RGB at a fixed S/L tuned for the dark theme (e.g. S=0.45, L=0.45). Register both in `CreatorCard.xaml` resources (or App resources).
- [ ] **Read `CreatorCard.xaml` first.** Today (≈line 69–73) the cover `Image{Binding Cover}` sits in a `Border Background="{StaticResource ThumbPlaceholderBrush}"`. Add, *inside that same Border, behind/over the Image*, a circular avatar shown only when `Cover` is null:
```xml
<Grid>
  <!-- avatar fallback: visible only when Cover is null -->
  <Border Visibility="{Binding Cover, Converter={StaticResource NullToVisibility}}"
          Background="{Binding Name, Converter={StaticResource StringToAvatarBrushConv}}"
          CornerRadius="999" Width="64" Height="64"
          HorizontalAlignment="Center" VerticalAlignment="Center">
    <TextBlock Text="{Binding Name, Converter={StaticResource StringToInitialsConv}}"
               Foreground="White" FontSize="24" FontWeight="Medium"
               HorizontalAlignment="Center" VerticalAlignment="Center"/>
  </Border>
  <Image Source="{Binding Cover}" Style="{StaticResource ThumbnailImage}" Stretch="UniformToFill"/>
</Grid>
```
(Reuse the existing `NullToVisibility` converter if present — the digest notes `NotNullToVisibility`/`CountToVisibility` exist from M18; use/define the null→Visible direction.) Keep the `ThumbPlaceholderBrush` border behind it.
- [ ] Build. Commit `feat(browse): circular creator-avatar fallback when no art`.

### Task A3: Populate real thumbnails on video cards (Home rails / Watchlist / Favorites / History / Search)
**Files:** `RecencyCardViewModel.cs`, `ContinueWatchingCardViewModel.cs`; DI in `ServiceCollectionExtensions.cs`; verify creation sites pass the loader.

> Today both expose `Cover => null` (hardcoded). They already carry `ThumbnailSeedPath` (the video file path). Mirror `CreatorCardViewModel.LoadImageAsync`: resolve `IThumbnailService.GetThumbnailPathAsync(seedPath, ct)` → `IImageLoader.Load(path, 200)`.

- [ ] **Read `CreatorCardViewModel.LoadImageAsync` (≈line 54–74)** for the exact `IThumbnailService` + `IImageLoader` usage. Change `RecencyCardViewModel` + `ContinueWatchingCardViewModel`: inject `IThumbnailService` + `IImageLoader` (optional trailing ctor params, default null — the M22 backward-compat pattern), replace `Cover => null` with `[ObservableProperty] ImageSource? _cover;` + an `async Task LoadImageAsync(CancellationToken)` that loads from `ThumbnailSeedPath`.
- [ ] **Find every construction site** of these two VMs (DiscoveryViewModel rails, SearchViewModel `VideoResults`, Favorites/Watchlist/History VMs) and ensure `LoadImageAsync` is invoked after creation (mirror how `CreatorsViewModel` triggers `CreatorCardViewModel.LoadImageAsync` — likely a fire-and-forget loop after the collection is populated). **STOP and report** if a creation site can't reach the services (would need a factory) — prefer threading the loader/thumbnail service through the owning VM's existing DI, as M22 did.
- [ ] **VideoCard.xaml neutral fallback:** read it; the null-`Cover` state shows the flat `ThumbPlaceholderBrush`. Add a small centered film glyph (`ui:SymbolIcon Symbol="Video24"` or `MoviesAndTv24`, ~24px, low opacity) visible only when `Cover` is null, so an art-less video card still reads as a video (NOT an avatar — videos aren't people).
- [ ] Build + (deferred to group close-out) sweep. Commit `feat(cards): load real thumbnails on video cards + neutral fallback glyph`.

### Group A close-out
- [ ] Test gate green. Build Release. **Real-image sweep** (ffmpeg fixtures) via a Sonnet subagent → TEXT verdict: creator cards show real art OR a colored initials avatar (never a blank tile); video cards on Home/Watchlist/Favorites/Search show real frames or the neutral glyph. Real-app `--seed-demo --view Browse`/`Home` launch `OK`.
- [ ] Push `feat/m23-a-card-art`, PR, CI green, merge, sync.

---

## Group B — Findable search (series results + counts + grouping) — PR #2

> Search already returns creators + videos. **Add series-level results, a result-count summary, grouped section headers, and a clear direct-play affordance.**

### Task B1: `SearchSeries` in Core — TDD
**Files:** `LibraryRepository.cs` (+ a `SeriesResult` record near `RecencyItem`); test in the existing repo-search tests.

- [ ] **Read `SearchVideos` (≈line 480) and `SearchCreators` (≈line 441)** to match style/params. Add:
```csharp
public IReadOnlyList<SeriesResult> SearchSeries(string query, int limit)
```
returning distinct series whose `base_title LIKE %query%` (excluding missing-only series), with fields `(long SeriesId, long SectionId, string Title, int EpisodeCount, string? ThumbnailSeedPath)` — `ThumbnailSeedPath` = first non-missing episode's `file_path`. `ORDER BY base_title LIMIT $limit`, `@`-params (the project uses `@`-style; confirm against `SearchVideos`).
- [ ] Test: seed via `StressLibrarySeeder` (or the existing search-test fixture), assert a known series title matches and `EpisodeCount` > 0; assert missing-only series excluded.
- [ ] Commit `feat(search): SearchSeries core query`.

### Task B2: Series results + counts + grouping in the UI
**Files:** `SearchViewModel.cs`, `SearchView.xaml`, new `SeriesResultViewModel`.

- [ ] **Read `SearchViewModel` (≈155 lines)** + `SearchView.xaml`. Add `ObservableCollection<SeriesResultViewModel> SeriesResults` + `HasSeriesResults`; call `_library.SearchSeries(query, 48)` alongside the existing two. A `SeriesResultViewModel` exposes `Title`, `EpisodeCountLabel`, `SectionId`, `SeriesId`, a `Cover` (load via the thumbnail service like A3), and an `OpenCommand` that raises an existing event to open that creator/series (reuse `OpenCreatorRequested` with `SectionId`, or add a play-all). 
- [ ] Add a **results summary line** at the top of `SearchView.xaml`: e.g. `"{CreatorResults.Count} creators · {SeriesResults.Count} series · {VideoResults.Count} videos"` (a computed `ResultSummary` string on the VM, recomputed when collections change). Add **section headers** ("Creators" / "Series" / "Videos") above each existing group, each collapsed when its collection is empty. Keep the existing `NoResults` empty state; make its copy explicit ("No creators, series, or videos match \"{query}\".").
- [ ] **Direct play:** video results already raise `PlayRequested`; ensure a single click/Enter on a video result plays it (verify the current affordance; if play is only via multi-select, add a direct Play button to the video result template — reuse `VideoCard`'s play affordance).
- [ ] Build + sweep (Search view with a query that hits all three groups — add a `--view Search` seed query in the harness if needed; **STOP and report** if the harness can't drive a populated Search state). Commit `feat(search): series results + result-count summary + grouped sections`.

### Group B close-out
- [ ] Test gate green; sweep verdict: Search shows Creators/Series/Videos groups with counts + a working empty state. Push `feat/m23-b-search`, PR, CI, merge, sync.

---

## Group C — Episode-row content (title + progress + runtime) — PR #3

> The #87 redesign is correct in spirit (Play · name · rating · watched; other actions in the right-click menu) but too sparse: title truncates and there's no progress/runtime. Bring those back **without** re-cluttering.

### Task C1: Carry duration + resume on episodes (Core read-path) — TDD
**Files:** `BrowseModels.cs` (`EpisodeView`), `LibraryRepository.cs` (`GetEpisodes` ≈line 306, `GetEpisode` ≈line 364).

- [ ] **Read `EpisodeView` + both queries first.** Add to the `EpisodeView` record **optional trailing params** (no ripple to other constructors): `double? Duration = null, double ResumePosition = 0`. Extend the `GetEpisodes`/`GetEpisode` SELECTs to also read `v.duration, v.resume_position` and map them. (Columns exist — no schema change.)
- [ ] Test: seed a video with a known `duration` + `resume_position` (use `SetDuration` + the resume writer), then `GetEpisodes(seriesId)` returns those values; an episode with null duration returns `Duration == null`.
- [ ] Commit `feat(core): episodes carry duration + resume_position`.

### Task C2: Episode-row VM progress/runtime + restored title
**Files:** `EpisodeViewModel.cs`, `SectionDetailView.xaml` (episode-row DataTemplate ≈line 408–590).

- [ ] In `EpisodeViewModel`: add `double ProgressFraction => Duration is > 0 ? Math.Clamp(ResumePosition / Duration.Value, 0, 1) : 0;`, `bool HasProgress => ProgressFraction is > 0 and < 1;`, and `string? RuntimeLabel => Duration is > 0 ? TimeSpan.FromSeconds(Duration.Value).ToString(...)` formatted `h:mm`/`m:ss` (mirror any existing duration formatter — search for one; if none, add a small `FormatRuntime` helper).
- [ ] **Read the current episode-row DockPanel.** The title `TextBlock` is the fill element with `TextTrimming=CharacterEllipsis` — it truncates because right-docked controls eat width at small sizes. Restructure the row so the **title gets dominant width** and add, compactly: a thin **progress bar** (reuse the M12 track+fill `Border` + `FractionToWidth` converter — search for it; Continue-watching uses it) shown when `HasProgress`, and a **runtime label** (`{Binding RuntimeLabel}`, muted) near the right. Keep: Play button, the half-star rating popup (#87), the watched checkbox, the right-click ContextMenu (favorite/watch-later/add-to-playlist). Do NOT add the old always-visible favorite/playlist icons back.
- [ ] Build + sweep (`--view SectionDetail` auto-expands a multi-episode series). Verdict: rows show full-enough title + a progress bar on the resumed episode + runtime, still uncluttered. Commit `feat(creator-page): episode rows show title + progress + runtime`.

### Group C close-out
- [ ] Test gate; sweep verdict PASS; real-app `--view SectionDetail` launch `OK`. Push `feat/m23-c-episode-rows`, PR, CI, merge, sync.

---

## Group D — Command palette: series results + more actions — PR #4

> Palette already does creator+video results + 11 actions. **Add series results and the missing actions.**

### Task D1: Series in the palette
**Files:** `CommandPaletteViewModel.cs`.
- [ ] **Read `RunAsync` (≈line 77).** Alongside `SearchCreators`+`SearchVideos`, call `SearchSeries(q, 10)` (from Group B) and add `PaletteItemKind.Series` items (extend the `PaletteItemKind` enum) whose `Execute` opens that creator/series (call `openSection`/the play funnel). Keep the sort: Action < Creator < Series < Video by kind, then score.
- [ ] Commit `feat(palette): series results`.

### Task D2: Missing action-registry entries
**Files:** `MainViewModel.cs` (`BuildActionRegistry` ≈line 492).
- [ ] Add entries: **"Library Health"** → `ShowMaintenanceCommand` (icon `Wrench24` or `Heart24`), **"New Smart View"** → the smart-views create/open command (find it on `SmartViewsViewModel`/`MainViewModel`), **"Add Source…"** → `Sources.AddSourceCommand`. (Skip context-dependent actions like "mark THIS watched" — the global palette has no current-item context; note this in the PR.)
- [ ] Commit `feat(palette): Library Health / New Smart View / Add Source actions`.

### Group D close-out
- [ ] Test gate (palette ranker/registry tests stay green; add a small test asserting the registry contains the 3 new labels). Sweep optional (palette is an overlay; `--view command-palette` exists). Push `feat/m23-d-palette`, PR, CI, merge, sync.

---

## Group E — Smart-view plain-English rules + live match count — PR #5

### Task E1: Pure `SmartRuleProse` renderer — TDD
**Files:** create `src/VideoShelf.Core/Discovery/SmartRuleProse.cs`; test `tests/VideoShelf.Core.Tests/Discovery/SmartRuleProseTests.cs`.

- [ ] **Read `SmartViewModels.cs` (`SmartRule(Field,Op,Value)`, `SmartViewDefinition(Match,Rules)`) + `SmartViewSqlBuilder`** for the exact field/op vocabulary: `tag{is,isNot}`, `creator{is,isNot}` (value = section id), `watched{is}` (true/false), `dateAdded{withinDays,beforeDays}`, `duration{gt,lt}` (seconds).
- [ ] Implement `Describe(SmartViewDefinition def, IReadOnlyDictionary<long,string>? creatorNames = null) → string`:
  - Match prefix: `"all"`→`"All of:"`, `"any"`→`"Any of:"`.
  - Per rule, plain English: `tag is X`→`"tagged X"`, `tag isNot X`→`"not tagged X"`; `creator is <id>`→`"by {creatorNames[id] ?? "creator #id"}"`; `watched is true/false`→`"watched"/"unwatched"`; `dateAdded withinDays N`→`"added in the last {N} days"` (and if N≥365, also fine as days); `dateAdded beforeDays N`→`"added more than {N} days ago"`; `duration gt N`→`"longer than {HumanDuration(N)}"`, `duration lt N`→`"shorter than {HumanDuration(N)}"`.
  - Join rules with `", "`.
- [ ] Tests cover each field/op + the match prefixes + an unknown field (fallback to the raw token, don't throw).
- [ ] Commit `feat(smartviews): plain-English rule renderer`.

### Task E2: Use prose in the list; live match count in the builder
**Files:** `SmartViewsViewModel.cs` (`BuildSummary` ≈line 67), `SmartViewRepository.cs` (+ count), `SmartViewsView.xaml`.
- [ ] Replace `BuildSummary`'s raw `$"{match}: {join(field op value)}"` with `SmartRuleProse.Describe(definition, creatorNameMap)` — pass a section-id→name map (build from `GetSectionSummaries`). The list `RuleSummary` binding (`SmartViewsView.xaml:62`) now reads as English.
- [ ] Live count: add `int CountMatchingVideos(SmartViewDefinition def, DateTimeOffset now)` to `SmartViewRepository` (a `SELECT COUNT(*)` over the same `SmartViewSqlBuilder` WHERE — reuse the builder; **STOP and report** if the builder only emits a full SELECT not reusable for COUNT). In the builder VM, recompute + show `"Matches N videos"` as rules change (debounce if needed).
- [ ] Build + sweep (`--view smart-views`). Verdict: rule reads as English + a match count shows. Commit `feat(smartviews): English summaries + live match count`.

### Group E close-out
- [ ] Test gate; sweep verdict PASS. Push `feat/m23-e-smartview-prose`, PR, CI, merge, sync.

---

## Group F — Visual polish — PR #6

### Task F1: Readable muted-text token (contrast)
**Files:** `DesignTokens.xaml`, label sites.
- [ ] Add `TextMutedBrush` = `#A8FFFFFF` (≈66% white — passes AA on the dark surfaces). Replace **ad-hoc `Opacity="0.6"`/`"0.7"` on text** with `Foreground="{StaticResource TextMutedBrush}"` (opacity removed) at: the `Caption` style (DesignTokens ≈line 168), History `WatchedAt` (`HistoryView.xaml`), smart-view `RuleSummary` (`SmartViewsView.xaml:62`), and any card caption/metadata using opacity-dimmed white text you find. Do NOT touch `TextSecondaryBrush`/`TextPrimaryBrush` or any palette color. **STOP and report** if a site uses opacity for non-text (e.g. a whole panel) — only convert text labels.
- [ ] Commit `fix(contrast): muted-text token replaces ad-hoc opacity on labels`.

### Task F2: Clearer Browse density glyphs
**Files:** `MainWindow.xaml` (≈line 233/241/248).
- [ ] Replace the ambiguous density icons (`TextGrammarArrowLeft24`/`Grid24`/`TextGrammarArrowRight24`) with a clearer set — e.g. compact=`TextBulletListLtr24` or `ListRtl24`, normal=`Grid24`, spacious=`GridDots24` (pick from valid `Wpf.Ui.Controls.SymbolRegular` members — verify each exists). Tooltips already present; keep them. (Optional: a small "Density" label before the group.)
- [ ] Commit `polish(browse): clearer density toggle glyphs`.

### Task F3: History — thumbnails + date grouping + progress
**Files:** `HistoryViewModel.cs`, `HistoryView.xaml`, `HistoryRowViewModel`.
- [ ] **Read both.** `HistoryRowViewModel` exposes `VideoId/Title/WatchedAt/PlayCommand` — add a `Cover` loaded via the thumbnail service (like A3; it has the video id → fetch the file_path via `GetEpisode(videoId)?.FilePath` or carry the seed path through the history query) and a `WatchedAtDate` (DateTimeOffset) for grouping. Group rows into "Today" / "This week" / "Older" (a `CollectionViewSource` with a `GroupDescription`, or pre-grouped `ObservableCollection`s). Switch the bare DockPanel rows to thumbnail cards matching the Favorites visual family. Add per-row progress if resume<duration (reuse C's progress).
- [ ] Build + sweep (`--view history`). Commit `feat(history): thumbnail cards + date grouping + progress`.

### Task F4: Playlists — auto-select first + real empty state
**Files:** `PlaylistsViewModel.cs` (`Load` ≈line 47–84), `PlaylistsView.xaml`.
- [ ] In `Load()`, after populating `Playlists`, if `Selected is null && Playlists.Count > 0` set `Selected = Playlists[0]` (mirror `CreatePlaylist`'s select). When the selected playlist has **no items**, show a proper empty state ("No videos in this playlist yet — add from Browse or a creator page.") instead of the items list. Keep the "Select a playlist…" text only for the genuinely-no-playlists case (or remove it since we auto-select).
- [ ] Build + sweep (`--view playlists`). Commit `feat(playlists): auto-select first + empty-state`.

### Task F5: Verify (then fix) the selection-mode checkbox glyph
**Files:** investigate `MainWindow.xaml` grid/list selection `CheckBox` (≈line 354/451).
- [ ] **Build + real-app launch `--view browse-selection`** (or drive selection mode) and look at an UNSELECTED card's checkbox via the sweep. If a stray "(" / broken glyph renders (WPF-UI `CheckBox` unchecked glyph at small size), fix it — e.g. give the `CheckBox` an explicit style/size, or replace with a plain bordered box. **If it renders fine (capture artifact), make NO code change and note it in the PR.** Do not invent a fix for a non-bug.
- [ ] Commit only if a real fix was needed: `fix(browse): selection checkbox glyph`.

### Group F close-out
- [ ] Test gate; sweep verdict across History/Playlists/Browse + a contrast spot-check. Push `feat/m23-f-polish`, PR, CI, merge, sync.

---

## Group G — Onboarding, demo truthfulness & ROADMAP flip — PR #7

### Task G1: Empty-state creator nudge + persistent Add-folder
**Files:** `MainWindow.xaml` (empty-state ≈line 551–565; nav/Library menu).
- [ ] Add a one-line nudge under the empty-state copy: "Tip: each folder you add becomes a **Creator** — name folders after the person." (Keep "Creator" — do NOT rename.)
- [ ] Add a persistent **"Add folder…"** entry to the **Library** nav menu (it already hosts Smart Views/Playlists/etc.) bound to `Sources.AddSourceCommand`, so adding a source doesn't require the gear. **Read the Library `Menu` block first** and match its `MenuItem` style.
- [ ] Commit `feat(onboarding): creator nudge + persistent Add-folder in Library menu`.

### Task G2: Reseed the stress fixture with person names
**Files:** `src/VideoShelf.App/Scale/StressLibrarySpec.cs`, `tests/VideoShelf.App.Tests/Scale/StressLibrarySpecTests.cs`.
- [ ] **Read `StressLibrarySpec.Generate` (≈line 21)** — creator names are `$"Creator {c:D4}"`. Replace with deterministic **person names** from a fixed first+last name pool (e.g. `FirstNames[i % F] + " " + LastNames[(i/F) % L] + (suffix if collision)`), still unique + deterministic for a given seed. Keep series/episode path codes as-is (they're fine). (`SeedAlphabetCreators` already uses person names — leave it.)
- [ ] Update `StressLibrarySpecTests` — the existing determinism/uniqueness asserts should still hold; adjust any that assumed the `"Creator NNNN"` format.
- [ ] Commit `chore(demo): person-named stress creators (reflect real usage)`.

### Task G3: Final verification + ROADMAP flip
- [ ] Full test gate green; record the new total (baseline 970 + new tests).
- [ ] **Full sweep** via a Sonnet subagent → TEXT verdict that all views render with the M23 improvements and no regression (creator avatars, video-card art, episode-row progress, search groups, English smart-view rules, History cards, Playlists auto-select, contrast). Real-app `--view SectionDetail`/`Home`/`Browse` launch `OK` (render-crash backstop).
- [ ] Optionally run `pwsh -File tools/harness/Run-ScaleBench.ps1` to confirm the avatar/thumbnail changes didn't regress Browse/SectionDetail node counts.
- [ ] Flip the M23 ROADMAP row to ✅ Merged (PR list #1–#7 actual numbers, final test count, one-line shipped summary) + a decision-log entry with the durable gotchas found (the avatar hue helper, the EpisodeView optional-field extension, the audit-calibration corrections, whether the selection glyph was real). Commit `docs(roadmap): M23 shipped`, ride Group G's branch.
- [ ] Push `feat/m23-g-onboarding-flip`, PR, CI green, merge `--merge --delete-branch`, sync.
- [ ] **Ping the owner** (Phase B handoff): M23 merged & CI-green.

---

## Acceptance criteria (whole milestone)
1. **No blank cards:** creator cards show real art or a colored initials avatar; video cards (Home/Watchlist/Favorites/History/Search) show real frames or a neutral video glyph — verified on a real-image sweep.
2. **Findable:** Search shows grouped Creators/Series/Videos with a result-count summary, a no-match empty state, and direct play; the command palette also reaches series + the new actions.
3. **Episode rows** show an untruncated-enough title + a progress bar (on resumed episodes) + runtime, while keeping the #87 decluttered action model.
4. **Smart-view rules** read as plain English with a live match count.
5. **Polish:** muted text meets AA (no ad-hoc opacity on labels), density glyphs are clearer, History has thumbnail cards + date grouping, Playlists auto-selects the first; the selection-checkbox glyph is verified (fixed only if real).
6. **Creator framing:** "Creator" kept everywhere; empty-state nudge explains the folder→creator model; a persistent Add-folder affordance exists; the stress fixture uses person names.
7. **Invariants:** no `user_version` runner; no `ui:*` retemplate / palette recolor (only the added muted-text brush); no `AutomationProperties`/screen-reader; library never written; full suite green (≥ 970 + new tests).

## STOP-and-report triggers (collected)
- A3: a video-card VM creation site can't reach `IThumbnailService`/`IImageLoader` without a new factory.
- B2: the harness can't drive a populated Search state for the sweep.
- C1: adding optional fields to `EpisodeView` unexpectedly ripples (a positional constructor somewhere breaks).
- E2: `SmartViewSqlBuilder` can't be reused for a `COUNT(*)`.
- F1: an `Opacity` site dims non-text (don't convert it).
- F5: the selection glyph renders fine (capture artifact) — make no change.
- Any episode-row / card / search restructure that still truncates, blanks, or crashes at real-app launch.

## Self-review (author)
- Every audit finding maps to a group; screenshot-misreads were calibrated (search/palette/toolbar/contrast/glyph narrowed). ✓
- Creator framing honored (avatar = people; videos get neutral glyph; "Creator" kept; demo reseeded). ✓
- Additive only; no migration (`EpisodeView` optional fields; columns exist). ✓
- New pure logic (`CreatorAvatar`, `SmartRuleProse`, `SearchSeries`, `CountMatchingVideos`) is unit-tested; XAML/wiring verified by sweep + real-app launch. ✓
