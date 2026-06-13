# M16 — Organize: video-level tags & smart views (full §A)

> **Written for Sonnet execution. If something doesn't match what's described here (a signature, a column, a file path), STOP and report rather than guess.** This codebase has bitten prior milestones with silent WPF binding-path fallbacks and ctor fan-out — verify before inventing.
>
> **Phase:** v4 M16 (first v4 milestone). **Branch:** `feat/organize-tags-smart-views`.
> **Scope decision (owner, 2026-06-13):** the **FULL icebox §A** — all 8 organize features — lands in M16. Owner explicitly chose the broadest scope over a tags-only slice.
> **Filter-axis decision (owner):** smart filters ship on axes populated **today** — tag, creator, watched-state, date-added, **duration**. **Resolution is OUT** (no width/height is captured; deferred to a probe extension in M18/M19).

---

## ⚠ SIZE WARNING & SPLIT SEAMS (read first)

This is a **3-milestones-worth** plan delivered as one M16 per the owner's "full §A" call. It is organized into **8 task groups (A–H)**, each independently shippable and tested. **Each group ends at a clean seam.** If the build session finds the whole thing too large for one PR/branch, **split at the group boundaries** in this order (later groups depend on earlier ones only as noted) and ship as `feat/organize-tags-smart-views` → multiple stacked PRs `M16a … M16h`, updating ROADMAP once at the end. **Do NOT split mid-group.** Dependency notes:

- **Group A (generalized tags)** is the foundation — B (smart views, tag axis) and F (bulk) build on it. Do A first.
- **Group B (smart views)** depends on A (tag axis) + existing `duration`/`watched`/`added_at`.
- **Groups C (favorites/ratings), D (playlists), E (watchlist), F (watch-history+bulk), G (custom cover), H (random)** are mutually independent and depend only on existing schema (+ A for the tag-aware bits of none of them — they're standalone). They can ship in any order after A/B or in parallel sub-PRs.
- **DI / `MainViewModel` ctor fan-out** is consolidated into **Task A0** (wire all new repos) and **Task I (final integration)** so each group doesn't re-touch DI. New page VMs are added to `MainViewModel` as their group lands.

**Test gate (every task):** `dotnet test VideoShelf.slnx -c Release --nologo -v q` — must stay green. Build quiet: `dotnet build VideoShelf.slnx -v minimal`.

---

## Architecture at a glance

**New Core tables** (all `CREATE TABLE IF NOT EXISTS` in `VideoShelfDb.Schema`; **no `EnsureColumn` needed for new tables** — only for new columns on existing tables):

```sql
-- A: generalized tagging (section_tags already exists, untouched)
series_tags(series_id INTEGER NOT NULL REFERENCES series(id) ON DELETE CASCADE,
            tag TEXT NOT NULL, PRIMARY KEY(series_id, tag));
video_tags (video_id  INTEGER NOT NULL REFERENCES videos(id)  ON DELETE CASCADE,
            tag TEXT NOT NULL, PRIMARY KEY(video_id, tag));

-- B: smart views
smart_views(id INTEGER PRIMARY KEY, name TEXT NOT NULL, definition TEXT NOT NULL,
            sort_order INTEGER NOT NULL DEFAULT 0,
            show_on_home INTEGER NOT NULL DEFAULT 1, created_at TEXT NOT NULL);

-- D: manual playlists
playlists(id INTEGER PRIMARY KEY, name TEXT NOT NULL,
          created_at TEXT NOT NULL, sort_order INTEGER NOT NULL DEFAULT 0);
playlist_items(playlist_id INTEGER NOT NULL REFERENCES playlists(id) ON DELETE CASCADE,
               video_id INTEGER NOT NULL REFERENCES videos(id) ON DELETE CASCADE,
               position INTEGER NOT NULL, PRIMARY KEY(playlist_id, video_id));

-- G: per-item cover overrides (path-only, DB-only mutation — library stays read-only)
video_art (video_id  INTEGER NOT NULL PRIMARY KEY REFERENCES videos(id)  ON DELETE CASCADE, image_path TEXT NOT NULL);
series_art(series_id INTEGER NOT NULL PRIMARY KEY REFERENCES series(id)   ON DELETE CASCADE, image_path TEXT NOT NULL);
```

**New columns on `videos`** (each via the existing `EnsureColumn(conn, "videos", col, def)` idempotent helper, added to `Migrate()` alongside the existing legacy-column block):

```
is_favorite       INTEGER NOT NULL DEFAULT 0   -- C
rating            INTEGER NOT NULL DEFAULT 0   -- C  (0 = unrated, 1..5)
in_watchlist      INTEGER NOT NULL DEFAULT 0   -- E
watchlist_at      TEXT                          -- E  (ISO; ordering watch-later by add time)
```

> **No `user_version` runner exists in this repo** (unlike AudioShelf) — schema is `CREATE TABLE IF NOT EXISTS` + `EnsureColumn` guards. Follow that exact pattern. Do **not** introduce a version runner.

**New repositories** (`src/VideoShelf.Core/Storage/`):
- `CurationRepository` — favorites, ratings, watchlist (all per-video flags on the new columns). **C + E.**
- `SmartViewRepository` — CRUD + `GetMatchingVideos`. **B.**
- `PlaylistRepository` — playlist + item CRUD/reorder. **D.**
- `HistoryRepository` — read over `watch_events`. **F.**
- `ItemArtRepository` — video/series cover overrides. **G.**
- **Extend `TagRepository`** with series/video methods + effective-tag resolution. **A.**
- **Extend `LibraryRepository`** with `GetRandomUnwatchedEpisode()`. **H.**

**New pure Core types** (`src/VideoShelf.Core/Discovery/` or `…/Models/`):
- `SmartViewDefinition(string Match, IReadOnlyList<SmartRule> Rules)` + `SmartRule(string Field, string Op, string Value)` — `record`s, `System.Text.Json`-serialized into `smart_views.definition`.
- `SmartViewSqlBuilder` — **pure, unit-tested**: compiles a `SmartViewDefinition` → `(string whereSql, IReadOnlyList<(string name, object value)> params)` over a `videos v JOIN series s JOIN sections sec` base. **All logic lives here so it's testable without a DB.**

**New App VMs / pages** (added to `MainViewModel` + `AppView` as each group lands):
- `TagEditorViewModel` (reusable, level-parameterized) — **A**.
- `SmartViewShelfViewModel` (Home shelf) + `SmartViewsViewModel` (manage page, `AppView.SmartViews`) — **B**.
- `PlaylistsViewModel` (`AppView.Playlists`) + `PlaylistItemViewModel` — **D**.
- `WatchlistViewModel` (`AppView.Watchlist`) — **E**.
- `FavoritesViewModel` (`AppView.Favorites`) — **C**.
- `HistoryViewModel` (`AppView.History`) — **F**.

**DI:** register every new repo + VM as `AddSingleton` in `ServiceCollectionExtensions.AddVideoShelf` (Task A0). `MainViewModel` ctor grows by the new page VMs (Task I batches the final wiring; each group's task adds its own).

---

## Existing facts the executor relies on (from the codebase digest — verify if a call fails)

- `TagRepository` (`src/VideoShelf.Core/Storage/TagRepository.cs`): section-level today — `GetTags/AddTag/RemoveTag/SetTags(long sectionId, …)`, `GetAllTags()`, `GetTagCounts()→TagCount(Tag,SectionCount)`, `static string Normalize(string)` (whitespace-collapse + lowercase). **Reuse `Normalize` for ALL new tag writes.**
- `VideoShelfDb` (`src/VideoShelf.Core/Storage/VideoShelfDb.cs`): `Open()` (fresh conn, `PRAGMA journal_mode=WAL` + `foreign_keys=ON`), `Migrate()` (schema-const + `EnsureColumn` block), `private static EnsureColumn(conn, table, column, definition)` (pragma_table_info guard). Schema is a raw-SQL `const`. Repos open a connection per call via `db.Open()`, parameterize with **`$`-prefixed** placeholders.
- `videos(id, series_id, file_path UNIQUE, episode_no, raw_filename, format, duration REAL, thumbnail_path, watched, missing, added_at, resume_position, resume_updated_at)`. `series(id, section_id, base_title, sort_key, is_standalone)`. `sections(id, source_id, folder_name, display_name)`. `watch_events(id, video_id, watched_at)`.
- `DiscoveryRepository` (`src/VideoShelf.Core/Discovery/`): rail queries return `RecencyItem`/`ContinueWatchingItem`/`SectionSuggestion`. `DiscoveryViewModel.LoadAsync()` (RailLimit=24) fetches all rails off the UI thread and fills `ObservableCollection`s via `Fill<>` helpers. **Add smart-view shelves here.** Ctor today: `(DiscoveryRepository discovery, LibraryRepository library, TagRepository tags, CreatorCardFactory cards, StatsRepository stats, PlayQueueViewModel playQueue)`.
- `RecencyItem(long VideoId, long SeriesId, long SectionId, string SeriesTitle, bool IsStandalone, int EpisodeNo, bool Watched, string? ThumbnailSeedPath)` in `VideoShelf.Core.Discovery`. `RecencyCardViewModel(RecencyItem)` exposes `Play` command + `PlayInvoked` event — **reuse for all video shelves/lists** (favorites, watchlist, playlist items, history, smart-view shelves).
- `SectionDetailViewModel` (`src/VideoShelf.App/ViewModels/`): ctor `(LibraryRepository, TagRepository, WatchRepository, IThumbnailService, CreatorArtRepository, IImagePicker, PlayQueueViewModel)`. Owns today's section-level tag editor (`Tags`/`Suggestions`/`TagInput`/`AddTag`/`AddSuggestion`/`RemoveTag`, `RefreshSuggestions` off `_allTags`). `SeriesViewModel` already bubbles `RequestRename`/`PlayAllRequested`/`EnqueueRequested`/`PlayNextRequested` events (mirror this for tag/cover/mark-watched affordances).
- `PlayQueueViewModel`: `StartSingle(ep)`, explicit-queue API (`Enqueue`, play-all). **Playlists/“play all” feed this** — a playlist's "Play all" builds the queue from its ordered `EpisodeView`s. `MainViewModel.OpenPlayer(ep)` is the single play path.
- `MainViewModel` ctor (12 params today): `(SourcesViewModel, LibraryViewModel, IScanCoordinator, PlayerViewModel, SettingsViewModel, DiscoveryViewModel, SectionDetailViewModel, RenameToolViewModel, CreatorsViewModel, SearchViewModel, MediaBackfillService, PlayQueueViewModel)`. Holds `AppView CurrentView` + the `Stack<AppView>` back-stack (`PushNav`, `ShowHome/Browse/Settings` clear it, detail/search push). **Every new page = a new `AppView` enum value + a `Show…()`/`Open…Async()` nav method + a host in `MainWindow.xaml` gated by `EnumToVisibility`/`EnumSetToVisibility` bound to `DataContext.CurrentView` (NOT `CurrentView` on the window — the M6 silent-fallback trap).**
- **M15 design system is live**: bind chips to `Chip`/`ChipToggle`, rail headers to `TypeRailHeader` (Title-Case) / `TypeEyebrow` (accent-caps), cards to `CardWidth`/`CardThumbHeight`, buttons to `PrimaryButton`/`SecondaryButton`/`TertiaryButton`, focus to `AppFocusVisual`. Icons via `ui:SymbolIcon` (only verified symbols — `PictureInPicture24` has no 16/20 variant; a wrong `SymbolRegular` member fails compile → pick nearest). **Additive only — never retemplate a WPF-UI control (caused 2 sibling regressions).** **Chip/pill fill = `ChipFillBrush` (#40FFFFFF), NEVER `SubtleFillBrush` (~6% alpha → reads as bare text).**
- **App-level converters** (`App.xaml`): `BoolToVisibility`, `EnumToVisibility` (key `EnumToVis`), `EnumSetToVisibility` (`EnumSetToVis`), `MissingToOpacity`, `FractionToWidth`. **No inverse-bool converter exists** — if you need "visible when empty", add one explicitly (don't inline-invent in XAML).
- **Cross-thread rule:** never `ConfigureAwait(false)` on a chain that ends mutating a UI-bound `ObservableCollection`. Heavy work in `Task.Run`, resume on the captured UI context. A `SynchronizationContext` regression test guards this.
- **Harness/sweep:** launch hooks `--folder/--autostart/--done-signal/--data-dir/--view/--play/--seed-demo`; `HarnessRunner` drives to a view + settles + signals. Sweep = `Run-VisualSweep.ps1` (**pwsh 7**, unlocked composited desktop, TOPMOST→NOTOPMOST toggle), PNGs viewed by a **Sonnet subagent returning a TEXT verdict** — never load PNGs into the controller. Close stray always-on-top media windows first (the recurring "Webcam Streams Recorder"/game-client bleed class).

---

## Task A — Generalized tagging (video + series), cascade resolution, reusable editor

**Foundation. Do first.**

### A0 — DI plumbing for all new repos (one-time)
In `ServiceCollectionExtensions.AddVideoShelf`, register (as `AddSingleton`, after the existing repo block): `CurationRepository`, `SmartViewRepository`, `PlaylistRepository`, `HistoryRepository`, `ItemArtRepository`. (VMs are registered with their groups.) These all take `VideoShelfDb` (and nothing else) — same shape as `TagRepository`. **Adding the classes empty-but-constructed here keeps later groups from re-touching DI.** Create each repo file with just the ctor + `VideoShelfDb _db` field initially; fill methods in each group's task.

### A1 — Schema: `series_tags` + `video_tags`
Add the two `CREATE TABLE IF NOT EXISTS` statements (above) to `VideoShelfDb.Schema`. Add indices `ix_series_tags_tag ON series_tags(tag)` and `ix_video_tags_tag ON video_tags(tag)` (tag-axis filtering scans by tag). **No `EnsureColumn` — these are new tables.**

### A2 — `TagRepository` extensions
Add (mirror the existing section methods exactly, `$`-params, `Normalize` every write):
```csharp
// series level
IReadOnlyList<string> GetSeriesTags(long seriesId);
void AddSeriesTag(long seriesId, string tag);
void RemoveSeriesTag(long seriesId, string tag);
void SetSeriesTags(long seriesId, IEnumerable<string> tags);
// video level
IReadOnlyList<string> GetVideoTags(long videoId);
void AddVideoTag(long videoId, string tag);
void RemoveVideoTag(long videoId, string tag);
void SetVideoTags(long videoId, IEnumerable<string> tags);
// resolution + universe
IReadOnlyList<string> GetEffectiveVideoTags(long videoId); // UNION of section_tags(its section) ∪ series_tags(its series) ∪ video_tags(self), distinct, sorted
IReadOnlyList<string> GetAllTagsAcrossLevels();            // distinct UNION of all three tables (for autocomplete + the tag-axis picker)
```
`GetEffectiveVideoTags` = one query joining `videos→series→sections` and `UNION`-ing the three tag tables for that video's ids. Keep `GetAllTags()` (section-only) intact for back-compat; new callers use `GetAllTagsAcrossLevels()`.

> **Cascade / "overridable defaults" — design decision (document in plan output + ROADMAP):** effective tags are the **additive union** across levels. The tag editor edits **one level at a time**; inherited tags render as read-only "inherited" chips (greyed, source-labelled). **Suppressing an inherited tag on a single child (a tombstone / negative tag) is OUT of M16** — it materially complicates every filter query. This is a deliberate simplification of "overridable." **STOP-and-report to the owner if true per-child suppression is required** rather than building tombstones unprompted.

### A3 — `TagEditorViewModel` (reusable, level-parameterized)
New `src/VideoShelf.App/ViewModels/TagEditorViewModel.cs`. Generalizes the section-only logic currently inline in `SectionDetailViewModel`:
```csharp
public enum TagLevel { Section, Series, Video }
public sealed partial class TagEditorViewModel : ObservableObject {
    public TagEditorViewModel(TagRepository tags);
    public void Load(TagLevel level, long targetId);      // loads applied tags for that level + inherited (read-only) chips for parents
    public ObservableCollection<string> Tags { get; }      // applied-at-this-level (removable)
    public ObservableCollection<InheritedTagViewModel> Inherited { get; } // parent-level, read-only, with a "from Creator/Series" label
    public ObservableCollection<string> Suggestions { get; }
    [ObservableProperty] string _tagInput;                 // OnTagInputChanged → RefreshSuggestions over GetAllTagsAcrossLevels()
    [RelayCommand] void AddTag();  [RelayCommand] void AddSuggestion(string t);  [RelayCommand] void RemoveTag(string t);
    public event Action? Changed;                          // raised on any mutation so hosts can refresh (e.g. re-resolve a video's effective tags)
}
```
`InheritedTagViewModel(string Tag, string SourceLabel)` is a trivial record-style VM. **Refactor `SectionDetailViewModel` to host a `TagEditorViewModel` (level=Section)** instead of its inline tag code — keep the public XAML-bound surface working (or update `SectionDetailView.xaml` bindings to `TagEditor.*`). **STOP-and-report if the SectionDetail tag bindings are deeply entangled** — if so, leave SectionDetail's inline editor as-is and just reuse `TagEditorViewModel` for the NEW series/video editors (lower risk; note the duplication).

### A4 — Wire series + video tag editors into the creator page
- **Series:** in the creator-page multi-episode expanded panel (`SeriesViewModel`), add a "Tags" affordance opening a `TagEditorViewModel(Series, seriesId)`. Gate behind the existing **Edit mode** (`SectionDetailViewModel.IsEditing`) like other metadata-editing affordances.
- **Video:** on the episode row inside an expanded series, add a small tag chip-row + "Edit tags" opening `TagEditorViewModel(Video, videoId)` (Edit-mode gated).
- Inherited chips display read-only with their source.

### A5 — Tests
`TagRepositoryTests` (Core): add/remove/set at series + video level; `GetEffectiveVideoTags` returns the correct union; `GetAllTagsAcrossLevels` distinct; `Normalize` applied. `TagEditorViewModelTests` (App): load per level, add/remove updates the right table, `Changed` fires, suggestions filter. **Expected +~10 Core, +~6 App.**

---

## Task B — Smart filters / saved views / virtual collections → Home shelves

**Depends on A (tag axis).**

### B1 — Pure definition types + SQL builder (Core, fully unit-tested)
`SmartViewDefinition(string Match /* "all"|"any" */, IReadOnlyList<SmartRule> Rules)`, `SmartRule(string Field, string Op, string Value)`.
Supported **fields/ops/values** (ship only these — the axes populated today):
| Field | Ops | Value |
|---|---|---|
| `tag` | `is`, `isNot` | tag string (matched against **effective** tags) |
| `creator` | `is`, `isNot` | `section_id` (as string) |
| `watched` | `is` | `"true"`/`"false"` |
| `dateAdded` | `withinDays`, `beforeDays` | integer days |
| `duration` | `gt`, `lt` | seconds (integer) |

`SmartViewSqlBuilder.Build(SmartViewDefinition def) → (string where, IReadOnlyList<(string,object)> @params)`:
- Base FROM is fixed by the repo (`videos v JOIN series s ON … JOIN sections sec ON …`), always `WHERE v.missing=0`.
- Each rule → a SQL fragment with `$p0,$p1,…` params; `tag` rules use `EXISTS (SELECT 1 FROM <effective-tags union> WHERE … = $pN)`; `creator` → `sec.id`; `watched` → `v.watched`; `dateAdded withinDays` → `v.added_at >= $cutoff` (compute cutoff string in the repo, pass `DateTimeOffset now` in — **do NOT call `DateTime.Now` in Core**, pass `now` like `GetForYou(int, DateTimeOffset)` does); `duration` → `v.duration > $p` (NULL-safe: `v.duration IS NOT NULL AND …`).
- Combine fragments with ` AND ` (match=all) or ` OR ` (match=any), wrapped in `(...)`.
- **Empty rules → no extra predicate** (the view matches all non-missing videos).
- **Pure, no DB** — unit-test the builder against expected SQL + param lists exhaustively.

### B2 — `SmartViewRepository`
```csharp
IReadOnlyList<SmartView> GetAll();                 // ordered by sort_order, id
IReadOnlyList<SmartView> GetHomeViews();           // WHERE show_on_home=1, ordered
long Create(string name, SmartViewDefinition def, bool showOnHome, DateTimeOffset now);
void Update(long id, string name, SmartViewDefinition def, bool showOnHome);
void Delete(long id);
void Reorder(long id, int sortOrder);
IReadOnlyList<RecencyItem> GetMatchingVideos(SmartViewDefinition def, int limit, DateTimeOffset now); // builder → SQL → RecencyItem rows
```
`SmartView(long Id, string Name, SmartViewDefinition Definition, int SortOrder, bool ShowOnHome, string CreatedAt)` record. JSON via `System.Text.Json` (`JsonSerializer.Serialize/Deserialize`); store/read `definition` as TEXT. `GetMatchingVideos` selects the same columns `RecencyItem` needs (reuse the existing recency projection — copy the SELECT list from `DiscoveryRepository.GetRecentlyAdded`).

### B3 — Home shelves
- `SmartViewShelfViewModel(SmartView view) { string Name; ObservableCollection<RecencyCardViewModel> Items; }`.
- `DiscoveryViewModel`: add `ObservableCollection<SmartViewShelfViewModel> SmartShelves`. In `LoadAsync` (inside the existing off-thread fetch), for each `smartViews.GetHomeViews()` call `GetMatchingVideos(def, RailLimit, now)`, build a shelf, fill on the UI context. Wire each card's `PlayInvoked` to the existing play path (same as RecentlyAdded). **Ctor +1 (`SmartViewRepository`)** → update `MainViewModelTestFactory` + `DiscoveryViewModelTests`.
- `DiscoveryView.xaml`: render `SmartShelves` as an `ItemsControl` of shelves (header `TypeRailHeader` + a horizontal card rail), placed below the existing rails. Each card = `views:VideoCard` wrapped per the M14 Pattern-A `Border`+`Tag` so context menus resolve.

### B4 — Smart-view management page (`AppView.SmartViews`)
- `SmartViewsViewModel` (`AppView.SmartViews`): list existing views (name + a one-line rule summary + show-on-home toggle + edit/delete/reorder), and a **builder** to create/edit: name field, match all/any toggle, an editable list of `SmartRuleRowViewModel` (field combo → op combo → value editor), Save / Cancel. Live preview count optional (call `GetMatchingVideos(def, 1?, now)` — keep simple: show match count via a `COUNT` overload if cheap, else skip preview).
- Nav: a "Smart Views" entry (top chrome or Home section header action) → `MainViewModel.ShowSmartViews()`. New host in `MainWindow.xaml`.
- **Ad-hoc → save:** the builder IS the creation path (icebox "named, persisted AND/OR"). A full Browse-level ad-hoc filter bar is OUT (that's M17's in-page filter) — M16 ships the saved-view builder + Home shelves.

### B5 — Tests
`SmartViewSqlBuilderTests` (Core, exhaustive: each field/op, all/any, empty, NULL-duration). `SmartViewRepositoryTests` (Core: CRUD round-trips JSON; `GetMatchingVideos` returns expected rows on a seeded DB; `GetHomeViews` filter). `SmartViewsViewModelTests` (App: add/remove rule rows, save creates, edit updates). **Expected +~14 Core, +~6 App.**

---

## Task C — Favorites / star ratings

**Independent (existing schema + new columns).**

- **Schema:** `EnsureColumn` `videos.is_favorite INTEGER NOT NULL DEFAULT 0`, `videos.rating INTEGER NOT NULL DEFAULT 0`.
- **`CurationRepository`:** `bool IsFavorite(long videoId)`, `void SetFavorite(long videoId, bool)`, `int GetRating(long videoId)`, `void SetRating(long videoId, int rating /*0..5, clamp*/)`, `IReadOnlyList<RecencyItem> GetFavorites(int limit)` (`WHERE is_favorite=1 AND missing=0`, order `resume_updated_at`/`added_at DESC`).
- **App:** `FavoritesViewModel` (`AppView.Favorites`) listing favorite videos (RecencyCard grid). A **heart toggle** + **1–5★ control** on the episode row and on `RecencyCardViewModel`/video context menus (add a `[RelayCommand] ToggleFavorite`/`SetRating` where the card's owner VM can reach `CurationRepository`). A **Favorites Home shelf** (or rely on a built-in smart view — but rating/favorite aren't smart-view fields this milestone, so ship an explicit Favorites shelf in `DiscoveryViewModel`).
- **Tests:** `CurationRepositoryTests` (favorite/rating round-trip, clamp, GetFavorites filter). App: favorites VM loads. **+~6 Core, +~3 App.**

---

## Task D — Manual playlists

**Independent.**

- **Schema:** `playlists` + `playlist_items` (above).
- **`PlaylistRepository`:** `long Create(string name, DateTimeOffset now)`, `void Rename(long id, string name)`, `void Delete(long id)`, `IReadOnlyList<Playlist> GetAll()`, `void AddItem(long playlistId, long videoId)` (append at `max(position)+1`, ignore dup PK), `void RemoveItem(long playlistId, long videoId)`, `void Move(long playlistId, long videoId, int newPosition)` (renumber in one tx — mirror `RenameExecutor`/`ReplaceChapters` tx style), `IReadOnlyList<EpisodeView> GetItems(long playlistId)` (ordered by position, `JOIN` to resolve `EpisodeView`, exclude `missing=1`). `Playlist(long Id, string Name, string CreatedAt, int ItemCount)` record.
- **App:** `PlaylistsViewModel` (`AppView.Playlists`): list playlists, open one → ordered items (reorder ▲▼, remove, **Play all** → builds the explicit `PlayQueueViewModel` from `GetItems` and `OpenPlayer(first)`). "Add to playlist…" on video context menus (a small picker of existing playlists + "New playlist…"). Reuse `QueueItemTemplate`/`QueueStyles.xaml` patterns for the ordered rows where it fits.
- **Tests:** `PlaylistRepositoryTests` (create/add/reorder/remove/delete cascade, position renumber, missing excluded). App: playlists VM add/reorder. **+~8 Core, +~4 App.**

---

## Task E — Watchlist / "watch later"

**Independent.**

- **Schema:** `EnsureColumn` `videos.in_watchlist INTEGER NOT NULL DEFAULT 0`, `videos.watchlist_at TEXT`.
- **`CurationRepository`** (extend C's repo): `bool InWatchlist(long videoId)`, `void SetWatchlist(long videoId, bool, DateTimeOffset now)` (set/clear `in_watchlist` + stamp/clear `watchlist_at`), `IReadOnlyList<RecencyItem> GetWatchlist(int limit)` (`WHERE in_watchlist=1 AND missing=0` order `watchlist_at DESC`).
- **App:** `WatchlistViewModel` (`AppView.Watchlist`) grid; "Add to / Remove from Watch later" on video context menus + episode rows; a **Watchlist Home shelf**. Distinct from passive Continue-watching (explicit intent).
- **Tests:** repo round-trip + GetWatchlist order; VM loads. **+~4 Core, +~2 App.**

---

## Task F — Watch-history view + bulk mark watched/unwatched

**Independent (reads existing `watch_events`).**

- **`HistoryRepository`:** `IReadOnlyList<HistoryEntry> GetHistory(int limit)` over `watch_events JOIN videos … JOIN series …` ordered `watched_at DESC`. `HistoryEntry(long VideoId, long SeriesId, string SeriesTitle, int EpisodeNo, bool IsStandalone, string WatchedAt, string? ThumbnailSeedPath)`.
- **Bulk mark watched/unwatched** (icebox: "stamp series watched without playing" — **not** full multi-select, which is M17): add `WatchRepository.SetWatchedForSeries(long seriesId, bool)` + `SetWatchedForSection(long sectionId, bool)` (bulk UPDATE in one tx; setting watched also appends a `watch_events` row per video when marking watched, mirroring single-mark semantics — verify how single `SetWatched` records the event and replicate). Affordance: creator-page series-tile + section context menu "Mark series/creator watched / unwatched".
- **App:** `HistoryViewModel` (`AppView.History`) list (each row → re-play). 
- **Tests:** `HistoryRepositoryTests` (order/limit/join), `WatchRepository` bulk tests (series/section set + event rows). App: history VM loads. **+~6 Core, +~3 App.**

> **STOP-and-report:** confirm exactly how the existing `WatchRepository.SetWatched` writes `watch_events` + clears `resume`/`resume_updated_at` before replicating it in bulk — do not guess the event/resume side-effects.

---

## Task G — Per-item custom cover / "set thumbnail from current frame"

**Independent. DB-only mutation — library folders never written (image saved into app data dir).**

- **Schema:** `video_art` + `series_art` (above).
- **`ItemArtRepository`:** `string? GetVideoArt(long videoId)` / `SetVideoArt(long, string path)` / `ClearVideoArt(long)`; same trio for series. Mirror `CreatorArtRepository` exactly (path-only).
- **Cover resolution precedence** (extend the existing card image logic): video card image = `video_art` → series seed/thumbnail → null; series tile = `series_art` → representative-video thumbnail. **Do NOT disturb `CreatorArtRepository`/creator-card resolution** (section art is unchanged).
- **"Set image…"** (file pick via existing `IImagePicker`) on series tile + episode row (Edit-mode gated).
- **"Set thumbnail from current frame"** in the player: the player already has frame capture (#8 screenshot). Route a new **"Set as cover"** transport/context action → snapshot the current frame to `AppPaths` data dir (a `covers/` subfolder — **never** the library folder) → `ItemArtRepository.SetVideoArt(currentVideoId, savedPath)`. Reuse the existing snapshot mechanism; confirm `PlayerViewModel` exposes the current video id (it plays an `EpisodeView` → `VideoId`).
- **Tests:** `ItemArtRepositoryTests` (round-trip/clear, cascade delete with video/series). App: cover-resolution precedence on the card VM. **+~5 Core, +~3 App.**

> **STOP-and-report:** verify the player snapshot API path (where #8 writes its PNG, and whether a current-frame grab is exposed off the playback engine) before wiring "Set as cover" — fail safe (never write into a library directory; if the only snapshot path available targets the library folder, STOP).

---

## Task H — Random / "Surprise me"

**Independent, trivial.**

- **`LibraryRepository.GetRandomUnwatchedEpisode() → EpisodeView?`**: `SELECT … WHERE v.watched=0 AND v.missing=0 ORDER BY RANDOM() LIMIT 1` projected to `EpisodeView` (reuse the `GetEpisodes` projection). Returns null if none.
- **App:** a **"Surprise me"** button in the top chrome / Home → `MainViewModel` resolves a random episode → `PlayEpisode`. No-op (disabled/toast) when null.
- **Tests:** repo returns an unwatched non-missing row or null on an all-watched DB. **+~2 Core, +~1 App.**

---

## Task I — Final integration, nav, harness sweep

- **`MainViewModel` ctor:** add the page VMs that landed (`SmartViewsViewModel`, `PlaylistsViewModel`, `WatchlistViewModel`, `FavoritesViewModel`, `HistoryViewModel`) + any repo a `MainViewModel` command needs directly (random, bulk-mark). Update `MainViewModelTestFactory` + every direct construction site (the M5/M8/M13/M14 fan-out precedent — expect ~4 test sites). Add the `AppView` enum values + `Show…()` nav + back-stack pushes + `MainWindow.xaml` hosts (gated by `DataContext.CurrentView`).
- **Nav surface:** these pages need entry points. Add a compact **"Library" menu / section** in the top chrome (or a Home "Manage" row) linking Smart Views · Playlists · Watch later · Favorites · History, plus the **Surprise me** action. Keep within the M11 chrome idiom (accent-underline active-nav, back-stack). **Don't reintroduce a persistent sidebar** (M11 deliberately killed it).
- **Harness:** add `--view` cases for `SmartViews`, `Playlists`, `Watchlist`, `Favorites`, `History` in `HarnessRunner` (seed via `--seed-demo`: create a demo smart view, a demo playlist, favorite + watchlist + tag a couple videos so each page renders non-empty — **extend `SeedDemoAsync`**, the recurring "new shelf renders empty in the sweep" gap from M7/M8/M9). Tag a video at video+series level so the cascade chips show.
- **Sweep:** `Run-VisualSweep.ps1` (pwsh 7, unlocked desktop, close stray media windows) → **Sonnet subagent text verdict** against per-view acceptance: smart-view Home shelf renders matching cards; SmartViews builder shows rule rows; video/series tag chips (incl. greyed inherited) render in Edit mode; favorites ★ + heart; playlist ordered rows + Play-all; watchlist grid; history list; Surprise-me reachable. **Never load PNGs into the controller.**
- **Whole-branch review** (`requesting-code-review` / `/code-review`) before merge — this is a large surface; prioritize: migration idempotency (run twice), tag-cascade query correctness, smart-view SQL-injection safety (params only — section ids inlined only if integer-validated), cross-thread collection rule, and the read-only-library invariant for "Set as cover".

---

## Acceptance criteria (milestone)

1. Tags exist at **video, series, and creator** levels; a video's **effective** tags are the union; inherited tags show read-only with their source; editing is level-scoped and Edit-mode gated. (Tombstone suppression documented OUT.)
2. **Smart views**: create/edit/delete/reorder named AND/OR filters over tag/creator/watched/date-added/duration; `show_on_home` views render as **Home shelves** of matching videos. Builder is SQL-injection-safe (parameterized).
3. **Favorites + 1–5★ ratings** per video; Favorites shelf/page.
4. **Manual playlists**: ordered, cross-creator; reorder/remove; **Play all** feeds the explicit play-queue.
5. **Watchlist** ("watch later"): explicit per-video flag; Watchlist shelf/page; distinct from Continue-watching.
6. **Watch-history** page over `watch_events`; **bulk mark watched/unwatched** at series + section level.
7. **Per-item custom cover** (file pick) + **"set cover from current frame"** in the player — image saved to app data dir, **library folders never written**.
8. **Surprise me** opens a random unwatched video.
9. **No migration regression** — `Migrate()` is idempotent (verified by running twice); new tables/columns via `CREATE TABLE IF NOT EXISTS` + `EnsureColumn` only. All tests green; screenshot sweep PASS (text verdict).

**Out of scope (deferred, documented):** resolution as a smart-filter axis (no width/height captured — M18/M19 probe extension); per-child tag suppression/tombstones; full multi-select bulk (M17); command palette (M17); template/cross-series bulk rename (M17).

---

## Estimated test delta
Core ≈ +55, App ≈ +28 → from **295** to **~378**. (Indicative; the real gate is green + sweep PASS, not a count.)
