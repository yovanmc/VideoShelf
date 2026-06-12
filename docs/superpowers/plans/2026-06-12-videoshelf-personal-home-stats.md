# M12 — Personal Home & Stats (Plan)

> **Written for Sonnet execution.** Each task is bite-sized: implement → write/extend the failing
> test → make it pass → run the gate → commit. **If anything here does not match the real code (a
> signature, member, table column, or libVLC API), STOP and report rather than guess.** This evolves
> the existing Discovery Home into a personal, creator-centric landing; M14 (design system) will later
> restyle it, so keep visuals additive and theming-rule-clean.

## Goal (from the ROADMAP M12 row + 2026-06-12 owner decision)
Make Home personal & real: **populate `duration`** (libVLC probe on scan) so **continue-watching
progress bars + watch-time stats become real**; **persist chapters** so the continue-watching card can
show the **chapter the resume falls in** (owner chose full chapter persistence over deferring to M18);
add a **watch-stats** strip (totals, watched-time, per-creator counts) on the SAME Home surface (extend
Discovery, don't fragment); and **rename the misleading "Recommended" rails honestly**. One cohesive
surface. Mirrors AudioShelf M11.

## Pre-locked findings from the code digest (do NOT re-investigate)
- **`duration` is ALREADY a `REAL` column on `videos`** (base `CREATE TABLE` in `VideoShelfDb`), just
  **never written** — `ScanService`/`LibraryRepository.UpsertVideo` omit it. So progress is always 0.
  (Add a defensive `EnsureColumn(conn,"videos","duration","REAL")` for pre-schema DBs.)
- **`ContinueWatchingCardViewModel.ProgressFraction` already computes** `Duration is >0 ? Clamp(Resume/Duration) : 0`
  from `ContinueWatchingItem.Duration` (`double?`), and `DiscoveryRepository.GetContinueWatching` already
  SELECTs `videos.duration`. **So populating `duration` lights up the progress bar with NO card change.**
- **Chapters are NOT persisted** and are NOT available from `Media.Parse` — only from a **live
  `MediaPlayer`** (`player.FullChapterDescriptions()`), i.e. a brief play per file (same cost class as
  `LibVlcThumbnailService`). So the probe MUST use a `MediaPlayer`, not just `Media.ParseAsync`.
- **`LibVlcThumbnailService`** (`src/VideoShelf.App/Services/LibVlcThumbnailService.cs`) already owns a
  `LibVLC` and does create-Media → create-MediaPlayer → Play → wait `Playing` → seek → snapshot →
  dispose. **The probe is modeled on THIS file** (read it; mirror its LibVLC construction, its
  Playing-wait with timeout, and its disposal/off-thread care).
- **Scan path is Core (no libVLC).** `MainViewModel.ScanAndReload` drives it:
  `await _scanCoordinator.ScanAllAsync(...)` → `Sources.Load()` → `await Library.LoadSectionsAsync()`
  → `await Discovery.LoadAsync()` → `await Creators.LoadAsync(...)` → `Settings.MarkScanned()`.
  **The duration/chapter backfill (App-layer, libVLC) hooks in AFTER `ScanAllAsync` and BEFORE
  `Discovery.LoadAsync()`.**
- **No stats surface exists** (greenfield). Aggregate idiom = `db.Open()` + `cmd.ExecuteScalar()` with
  `$`-named params (mirror `LibraryRepository`).
- **`DiscoveryViewModel`** ctor = `(DiscoveryRepository discovery, LibraryRepository library,
  TagRepository tags, CreatorCardFactory cards)`; rails are `ObservableCollection<…>` with `Has…`
  bools + `RailLimit = 24`; `LoadAsync()` runs queries in a `Task.Run`. Rails (DiscoveryView.xaml, in
  order): **"Continue watching"** (`VideoCard`←`ContinueWatchingCardViewModel`), **"Recommended
  creators"** (`CreatorCard`←`RecommendedCreators`, from `GetForYou`), **"Recommended videos"**
  (`VideoCard`←`RecommendedVideos`, from `GetRecommendedVideos`, unwatched), **"Recently added"**,
  **"Recently watched"** (inline button templates), **"Pick a tag"**.
- **`ChapterRecord` does not exist yet**; `ChapterOption(int Index,string Name,double StartSeconds)` is
  the *playback* record in `IPlaybackEngine.cs` (live only). M12 adds a persisted `ChapterRecord` in
  Core.Models.
- **DI** = all `AddSingleton` in `ServiceCollectionExtensions.AddVideoShelf`.
- Adding a ctor param to `MainViewModel`/`DiscoveryViewModel` fans out to their test construction sites
  (`MainViewModelTestFactory`, the inline `MainViewModel` test sites, and `DiscoveryViewModelTests.Fx`) —
  **expected; update them** (this is fine, unlike M11 which deliberately avoided it).

## Design decisions (made; don't re-decide)
1. **One probe pass, player-based:** `IMediaProbe.ProbeAsync(path)` opens the file in a brief-play
   `MediaPlayer` and returns BOTH `DurationSeconds` (`player.Length` ms→s) and `Chapters`
   (`player.FullChapterDescriptions()`). Duration alone could use `Media.Parse`, but since chapters
   force a player anyway, get both from the one player.
2. **Backfill is incremental + crash-safe/resumable:** only `WHERE duration IS NULL AND missing=0`;
   each video's `SetDuration` + `ReplaceChapters` committed independently; per-file errors are caught
   and skipped; re-running resumes (honors the standing destructive-op/resumable discipline).
3. **Chapter label on the continue card** = the last chapter whose `StartSeconds <= ResumePosition`;
   shown as the chapter `Name` if non-empty else `"Chapter {Index+1}"`. Absent when the video has no
   chapters. Resolved in `DiscoveryViewModel` via `GetChapters(videoId)` (≤24 small queries — fine).
4. **Stats live ON Home** (extend `DiscoveryViewModel` + a header section in `DiscoveryView`), NOT a
   separate page: total videos, watched count, total watched-time, in-progress count, + a small
   **top-creators-by-watched** list. Zero-safe.
5. **Honest rail names:** "Recommended creators" → **"Creators"**; "Recommended videos" → **"More to
   watch"** (it's unwatched videos). No conditional naming — keep it simple and honest.
6. **Progress bars:** only continue-watching needs one (its whole purpose); it already exists and just
   needs real `duration`. Do NOT add progress to recently-added (unwatched=0) / recently-watched
   (done) — out of scope.

## Conventions (from the runbook)
- Worktree under `.worktrees/`; branch `feat/personal-home-stats`. Gate: `dotnet test VideoShelf.slnx
  -c Release --nologo -v q`. Build quiet: `dotnet build … -v minimal`. `gh` at
  `& "C:\Program Files\GitHub CLI\gh.exe"`. Merge `--merge` from the main repo root. Commit author
  `yovanmc` + trailer `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>` (BOM-free, no Codex
  trailer). One commit per task.
- **Theming rule (binds):** additive only — no `Style`/`ControlTemplate` override of a WPF-UI control
  for cosmetics. The stats strip is plain panels/`TextBlock`s + existing DesignTokens styles.
- **libVLC/WPF testability pattern (mirror):** put ALL logic in plain VMs/services unit-tested with a
  FAKE (`IMediaProbe` faked in tests); keep the concrete `LibVlcMediaProbe` THIN and **uncovered by
  unit tests** (integration, verified by the Phase-9 harness sweep). NEVER `ConfigureAwait(false)` on a
  chain that ends by mutating a UI-bound `ObservableCollection`.
- Known **single-test parallel flake**: if exactly one *unrelated* test fails, re-run that project in
  isolation to confirm before reporting.

---

## Task 1 — Core schema: `video_chapters` table + `duration` safety column

**File:** `src/VideoShelf.Core/Storage/VideoShelfDb.cs` (the `Migrate()` method + its `EnsureColumn` idiom).

First READ `Migrate()` and `EnsureColumn`. Add, in `Migrate()`:
- A safety `EnsureColumn(conn, "videos", "duration", "REAL");` (pre-schema DBs may lack it).
- A new table (mirror the existing `CREATE TABLE IF NOT EXISTS` style + FK-CASCADE used by `videos`):
```sql
CREATE TABLE IF NOT EXISTS video_chapters (
    video_id     INTEGER NOT NULL,
    idx          INTEGER NOT NULL,
    name         TEXT    NOT NULL DEFAULT '',
    start_seconds REAL   NOT NULL DEFAULT 0,
    PRIMARY KEY (video_id, idx),
    FOREIGN KEY (video_id) REFERENCES videos(id) ON DELETE CASCADE
);
```
> Match the real DDL style (quoting, FK clause) used for `videos`/`series`. If FKs require
> `PRAGMA foreign_keys=ON` to be set somewhere, confirm the existing tables rely on it and follow suit.

**Test** (`tests/VideoShelf.Core.Tests/…` beside existing schema/migration tests, using `TempDb`):
`Migrate_creates_video_chapters_table_and_duration_column` — after `new TempDb()`, query
`pragma_table_info('videos')` contains `duration`, and `SELECT name FROM sqlite_master WHERE type='table'
AND name='video_chapters'` returns a row. (Mirror how existing migration tests introspect the schema.)

**Verify** gate green. **Commit** `M12: video_chapters table + duration safety column`.

---

## Task 2 — Core: persist/read duration + chapters; list videos needing a probe

**Files:** `src/VideoShelf.Core/Models/ChapterRecord.cs` (new) + `src/VideoShelf.Core/Storage/LibraryRepository.cs` (+ tests).

### 2a. New record (Core.Models)
```csharp
namespace VideoShelf.Core.Models;

/// <summary>A persisted chapter marker for a video (probed from libVLC at scan time).</summary>
public sealed record ChapterRecord(int Index, string Name, double StartSeconds);
```
Add a small probe-target record too (place next to it or in an existing models file — match where
`EpisodeView`/`SeriesSummary` live, i.e. `VideoShelf.Core.Models`):
```csharp
/// <summary>A video that still needs a libVLC duration/chapter probe.</summary>
public sealed record VideoToProbe(long Id, string FilePath);
```

### 2b. `LibraryRepository` methods (mirror its `db.Open()` + `$`-param idiom; one `conn` per call)
```csharp
public IReadOnlyList<VideoToProbe> GetVideosNeedingDuration()
// SELECT id, file_path FROM videos WHERE duration IS NULL AND missing = 0 ORDER BY id

public void SetDuration(long videoId, double seconds)
// UPDATE videos SET duration = $d WHERE id = $id

public void ReplaceChapters(long videoId, IReadOnlyList<ChapterRecord> chapters)
// In ONE transaction: DELETE FROM video_chapters WHERE video_id=$id; then INSERT each
// (video_id, idx, name, start_seconds). Idempotent on re-probe. If chapters is empty, just the DELETE.

public IReadOnlyList<ChapterRecord> GetChapters(long videoId)
// SELECT idx, name, start_seconds FROM video_chapters WHERE video_id=$id ORDER BY idx
// → new ChapterRecord(idx, name, start_seconds)
```
> Use the file's existing transaction pattern if one exists (e.g. a `BeginTransaction`); otherwise wrap
> the DELETE+INSERTs in a `using var tx = conn.BeginTransaction();` … `tx.Commit();`. Match the real API.

**Tests** (Core.Tests, `TempDb`, seed via `UpsertSource/Section/Series/Video`):
1. `GetVideosNeedingDuration_returns_only_null_duration_present_videos` — seed 2 videos, `SetDuration`
   on one, mark the other `missing` via the repo's missing-setter if available (else seed a 3rd present
   one); assert only the still-null, non-missing video is returned.
2. `SetDuration_persists` — `SetDuration(id, 123.5)`, then read it back (via `GetVideo`/`GetVideos` or a
   direct `duration` read) ≈ 123.5.
3. `ReplaceChapters_then_GetChapters_roundtrips_and_replaces` — write 3 chapters, read back equal; write
   2 different chapters for the same video, assert `GetChapters` now returns exactly those 2 (replaced).

**Verify** gate green. **Commit** `M12: persist + read video duration and chapters`.

---

## Task 3 — Core: watch-stats repository

**File:** `src/VideoShelf.Core/Storage/StatsRepository.cs` (new) + tests. Register in DI in Task 4's
DI step OR here — add `services.AddSingleton<StatsRepository>();` to `AddVideoShelf` (confirm the file).

### 3a. Stat models (Core.Models)
```csharp
public sealed record LibraryStats(int TotalVideos, int WatchedVideos, int InProgressVideos, double WatchedDurationSeconds);
public sealed record CreatorWatchCount(long SectionId, string Name, int WatchedCount);
```

### 3b. `StatsRepository(VideoShelfDb db)` (mirror `LibraryRepository`'s `db.Open()`/`$`-param/`ExecuteScalar`)
```csharp
public LibraryStats GetLibraryStats()
// total       = SELECT COUNT(*) FROM videos WHERE missing = 0
// watched     = SELECT COUNT(*) FROM videos WHERE watched = 1 AND missing = 0
// inProgress  = SELECT COUNT(*) FROM videos WHERE resume_position IS NOT NULL AND missing = 0
// watchedSecs = SELECT COALESCE(SUM(duration),0) FROM videos WHERE watched = 1 AND missing = 0
// (run as scalars; cast counts via Convert.ToInt32, secs via Convert.ToDouble)

public IReadOnlyList<CreatorWatchCount> GetTopCreatorsByWatched(int limit)
// SELECT s.id, s.display_name, COUNT(v.id) AS c
//   FROM sections s
//   JOIN series se ON se.section_id = s.id
//   JOIN videos v  ON v.series_id  = se.id
//  WHERE v.watched = 1 AND v.missing = 0
//  GROUP BY s.id, s.display_name
//  HAVING c > 0
//  ORDER BY c DESC, s.display_name ASC
//  LIMIT $limit
```
> **Confirm the REAL table/column names** by reading `VideoShelfDb`/`LibraryRepository`: the section
> display-name column (`display_name`?), the series→section FK column (`section_id`?), and the
> videos→series FK (`series_id`?). If any differ, mirror the real names and STOP-and-report if the
> join can't be expressed.

**Tests** (Core.Tests, `TempDb`): seed a source→section(s)→series→videos with a mix of watched/
resume/duration, then:
1. `GetLibraryStats_counts_and_sums` — assert TotalVideos, WatchedVideos, InProgressVideos, and
   WatchedDurationSeconds match the seeded data (set some durations via `SetDuration`, mark some watched
   via `WatchRepository.SetWatched`, set a resume via `SetResumePosition`).
2. `GetTopCreatorsByWatched_orders_by_count_desc` — two creators with different watched counts → the
   higher first; a creator with 0 watched is excluded.

**Verify** gate green. **Commit** `M12: StatsRepository (library totals + top creators by watched)`.

---

## Task 4 — App: `IMediaProbe` + `LibVlcMediaProbe` + DI

**Files:** `src/VideoShelf.App/Services/IMediaProbe.cs` (new), `src/VideoShelf.App/Services/LibVlcMediaProbe.cs`
(new), `ServiceCollectionExtensions.cs` (register).

First READ `LibVlcThumbnailService.cs` fully and mirror its LibVLC construction + Playing-wait + disposal.

### 4a. Interface + result (App.Services; result references Core.Models.ChapterRecord)
```csharp
using VideoShelf.Core.Models;
namespace VideoShelf.App.Services;

public sealed record MediaProbeResult(double? DurationSeconds, IReadOnlyList<ChapterRecord> Chapters);

public interface IMediaProbe
{
    /// <summary>Briefly opens the file in libVLC to read its duration and chapter markers.
    /// Returns null duration if it can't be determined. Never throws for a bad file — returns
    /// (null, empty).</summary>
    Task<MediaProbeResult> ProbeAsync(string path, CancellationToken cancellationToken);
}
```

### 4b. `LibVlcMediaProbe : IMediaProbe`
- Own a `LibVLC` like the thumbnail service (`new LibVLC("--no-audio","--no-video-title-show","--quiet")`).
- In `ProbeAsync`: create `new Media(_libVlc, new Uri(path))`; `new MediaPlayer(media)`; `Play()`; wait
  for the `Playing` event (TaskCompletionSource + a ~10s timeout / `cancellationToken`), mirroring the
  thumbnail service's wait. Once playing:
  - `var lenMs = player.Length;` → `DurationSeconds = lenMs > 0 ? lenMs / 1000.0 : (double?)null;`
  - Read chapters: in the pinned LibVLCSharp, the call is `player.FullChapterDescriptions()` (or
    `player.FullChapterDescriptions(int)` for a title). Map each to
    `new ChapterRecord(i, desc.Name ?? "", desc.TimeOffset / 1000.0)` where `TimeOffset` is ms.
    **STOP-and-report if the `FullChapterDescriptions` shape / `TimeOffset`/`Name` members differ** in
    the pinned API (do not guess the chapter API).
  - `player.Stop();` dispose `player` and `media` (in `finally`). Be off-thread-safe; do not deadlock by
    calling back into libVLC from its own event thread — set the TCS from the `Playing` handler only.
- Wrap the whole body so a failure returns `new MediaProbeResult(null, Array.Empty<ChapterRecord>())`
  rather than throwing.

### 4c. DI
`services.AddSingleton<IMediaProbe, LibVlcMediaProbe>();` in `AddVideoShelf`.

**No unit test** for the concrete service (integration — verified by the harness). It builds.

**Verify** builds + gate green. **Commit** `M12: IMediaProbe + LibVlcMediaProbe (duration + chapters)`.

---

## Task 5 — App: `MediaBackfillService` + wire into the scan

**Files:** `src/VideoShelf.App/Services/MediaBackfillService.cs` (new), `MainViewModel.cs` (+1 ctor dep),
`ServiceCollectionExtensions.cs`, and the test construction sites.

### 5a. `MediaBackfillService(LibraryRepository library, IMediaProbe probe)`
```csharp
/// <summary>Probes every video still missing a duration and persists its duration + chapters.
/// Incremental (only duration IS NULL), crash-safe (each video committed independently),
/// resumable (re-running picks up whatever is still null). Per-file errors are skipped.</summary>
public async Task BackfillAsync(CancellationToken cancellationToken)
{
    var pending = library.GetVideosNeedingDuration();
    foreach (var v in pending)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var r = await probe.ProbeAsync(v.FilePath, cancellationToken).ConfigureAwait(false);
            if (r.DurationSeconds is > 0) library.SetDuration(v.Id, r.DurationSeconds.Value);
            library.ReplaceChapters(v.Id, r.Chapters);
        }
        catch (OperationCanceledException) { throw; }
        catch { /* skip this file; a later scan retries it */ }
    }
}
```
> `ConfigureAwait(false)` here is SAFE — `BackfillAsync` does not touch any UI-bound `ObservableCollection`
> (it only writes via the repo). The UI reload happens afterward in `Discovery.LoadAsync()`.

### 5b. Register + wire
- `services.AddSingleton<MediaBackfillService>();`
- Add `MediaBackfillService backfill` as a new `MainViewModel` ctor param; store it. In `ScanAndReload`,
  **after** `await _scanCoordinator.ScanAllAsync(token)` and **before** `await Discovery.LoadAsync()`,
  add `await backfill.BackfillAsync(token);` (inside the existing `try`, under the `IsScanning` guard so
  the progress ring covers it). Match the real local/field names in `ScanAndReload`.
- Update **`MainViewModelTestFactory.Create`** and any inline `MainViewModel` construction in tests to
  pass a `MediaBackfillService` built with a **fake/no-op `IMediaProbe`** (returns
  `new MediaProbeResult(null, Array.Empty<ChapterRecord>())`). Add a tiny `FakeMediaProbe` to the App
  test support if none fits.

**Tests** (App.Tests): `BackfillAsync_populates_duration_and_chapters` — with a `FakeMediaProbe`
returning `(120.0, [ChapterRecord(0,"Intro",0), ChapterRecord(1,"Part 2",60)])`, seed 1 present video
with null duration, run `BackfillAsync`, assert `GetVideosNeedingDuration()` is now empty and
`GetChapters(id)` has 2 rows; running it again is a no-op (nothing still needs duration).

**Verify** gate green. **Commit** `M12: MediaBackfillService populates duration + chapters on scan`.

---

## Task 6 — App: watch-stats on the Home surface

**Files:** `DiscoveryViewModel.cs` (+1 ctor dep `StatsRepository`), `DiscoveryView.xaml`, +
`DiscoveryViewModelTests.cs` `Fx`.

### 6a. `DiscoveryViewModel`
- Add `StatsRepository stats` to the ctor; store it. Add members:
```csharp
[ObservableProperty] private string _watchedSummary = "";   // e.g. "12 of 40 watched · 8h 30m"
[ObservableProperty] private string _inProgressSummary = ""; // e.g. "3 in progress"
public ObservableCollection<CreatorWatchCount> TopCreators { get; } = new();
public bool HasStats { get; private set; }
```
- In `LoadAsync` (inside the existing `Task.Run` for the queries, then assign on the UI continuation —
  mirror how the rails are filled): read `var s = stats.GetLibraryStats();` and
  `var top = stats.GetTopCreatorsByWatched(5);`. Build the strings:
  - `WatchedSummary = $"{s.WatchedVideos} of {s.TotalVideos} watched · {FormatDuration(s.WatchedDurationSeconds)}";`
  - `InProgressSummary = s.InProgressVideos == 0 ? "" : $"{s.InProgressVideos} in progress";`
  - Replace `TopCreators` contents with `top`. `HasStats = s.TotalVideos > 0;` (raise `OnPropertyChanged(nameof(HasStats))`).
  - Add a private `static string FormatDuration(double seconds)` → `"{h}h {m}m"` (or `"{m}m"` when <1h, `"0m"` when 0).
> Keep the stats assignment on the captured UI context (NO `ConfigureAwait(false)` on the chain that
> mutates `TopCreators`).

### 6b. `DiscoveryView.xaml` — a stats header section at the TOP of the content (above "Continue watching")
A plain panel (additive; existing DesignTokens styles only), visible when `HasStats`:
```xml
<StackPanel Margin="0,0,0,16" Visibility="{Binding HasStats, Converter={StaticResource BoolToVisibility}}">
    <TextBlock Text="{Binding WatchedSummary}" FontSize="16" FontWeight="SemiBold" />
    <TextBlock Text="{Binding InProgressSummary}" Opacity="0.7" Margin="0,2,0,0"
               Visibility="{Binding InProgressSummary, Converter={StaticResource ...}}" /> <!-- see note -->
    <ItemsControl ItemsSource="{Binding TopCreators}" Margin="0,8,0,0">
        <ItemsControl.ItemsPanel><ItemsPanelTemplate><StackPanel Orientation="Horizontal"/></ItemsPanelTemplate></ItemsControl.ItemsPanel>
        <ItemsControl.ItemTemplate>
            <DataTemplate>
                <Border Background="{StaticResource SubtleFillBrush}" CornerRadius="12" Padding="10,4" Margin="0,0,8,0">
                    <TextBlock><Run Text="{Binding Name}" /><Run Text=" · " /><Run Text="{Binding WatchedCount}" /></TextBlock>
                </Border>
            </DataTemplate>
        </ItemsControl.ItemTemplate>
    </ItemsControl>
</StackPanel>
```
> For the `InProgressSummary` visibility: an empty string should hide that line — if the project has a
> string-empty→collapsed converter use it; otherwise simplest is to bind `Visibility` to a new
> `bool HasInProgress` property (`InProgressVideos>0`) via `BoolToVisibility`. Pick the simplest that
> builds; don't invent a converter that doesn't exist. Confirm `SubtleFillBrush` is the real key (it is,
> used in MainWindow.xaml).

**Test** (`DiscoveryViewModelTests`): update `Fx` to construct + pass a `StatsRepository`. Add
`Stats_populate_on_load` — seed videos (some watched with durations), `await vm.LoadAsync()`, assert
`vm.HasStats` true, `WatchedSummary` contains the watched count, and `TopCreators` is non-empty.

**Verify** gate green. **Commit** `M12: watch-stats strip on Home (totals + top creators)`.

---

## Task 7 — App: chapter label on the continue-watching card

**Files:** `ContinueWatchingCardViewModel.cs`, `DiscoveryViewModel.cs` (where it builds continue cards),
`Views/VideoCard.xaml`.

### 7a. `ContinueWatchingCardViewModel`
Add a constructor/property path for an optional chapter label (match how the VM is currently constructed
in `DiscoveryViewModel.MakeContinueCard`):
```csharp
public string? ChapterLabel { get; init; }       // null when the video has no chapters
public bool HasChapter => !string.IsNullOrEmpty(ChapterLabel);
```
(If the VM is built positionally, add an optional ctor param `string? chapterLabel = null` and assign;
keep existing callers working.)

### 7b. `DiscoveryViewModel` — resolve the label when building each continue card
Where continue cards are made (in `LoadAsync`/`MakeContinueCard`), for each `ContinueWatchingItem item`:
```csharp
var chapters = library.GetChapters(item.VideoId);
string? label = null;
if (chapters.Count > 0)
{
    ChapterRecord? cur = null;
    foreach (var c in chapters) { if (c.StartSeconds <= item.ResumePosition) cur = c; else break; }
    if (cur is not null) label = string.IsNullOrEmpty(cur.Name) ? $"Chapter {cur.Index + 1}" : cur.Name;
}
```
and pass `label` into the card. (Chapters come back ordered by `idx`; the loop relies on that.)
> This runs inside the existing background `Task.Run` query phase (≤24 items) — fine. Do the small
> `GetChapters` reads there, build the labels, then create/assign the cards on the UI continuation as the
> existing code already does.

### 7c. `VideoCard.xaml` — show the chapter under the title (continue-watching cards only)
Add a `TextBlock` bound to `ChapterLabel`, collapsed when absent:
```xml
<TextBlock Text="{Binding ChapterLabel}" FontSize="11" Opacity="0.75" TextTrimming="CharacterEllipsis"
           Visibility="{Binding HasChapter, Converter={StaticResource BoolToVisibility}}" />
```
> `VideoCard` is bound to BOTH `ContinueWatchingCardViewModel` (has `ChapterLabel`/`HasChapter`) and
> `RecencyCardViewModel` (does NOT). A binding to a missing property on `RecencyCardViewModel` silently
> resolves to null/Collapsed — so the new `TextBlock` is simply hidden on "Recommended/More-to-watch"
> cards. Confirm `RecencyCardViewModel` has no `HasChapter`; the binding will harmlessly evaluate false.
> If WPF logs a binding error you want to avoid, add `HasChapter => false` + `ChapterLabel => null` to
> `RecencyCardViewModel` for a clean bind. Prefer the clean-bind (add the two read-only members).

**Test** (`DiscoveryViewModelTests`): `Continue_card_shows_chapter_for_resume_position` — seed a video,
`SetResumePosition(id, 65)`, `ReplaceChapters(id, [ChapterRecord(0,"Intro",0), ChapterRecord(1,"Part 2",60)])`,
`SetDuration(id, 120)`, `await vm.LoadAsync()`, assert the continue card's `ChapterLabel == "Part 2"` and
`HasChapter`. Also a no-chapters video → `ChapterLabel` null / `HasChapter` false.

**Verify** gate green. **Commit** `M12: chapter-granular label on the continue-watching card`.

---

## Task 8 — App: honest rail names + verify real progress (DiscoveryView)

**File:** `Views/DiscoveryView.xaml` (text + a small confirm).

- Rename the rail header `TextBlock`s: **"Recommended creators" → "Creators"**, **"Recommended videos"
  → "More to watch"**. Leave the bindings (`RecommendedCreators`/`RecommendedVideos`, `Has…`) unchanged
  — only the visible header strings change.
- Confirm the **"Continue watching"** rail uses `<views:VideoCard/>` and that `VideoCard.xaml` already
  renders a `ProgressBar`/progress visual bound to `ProgressFraction` (it does per the digest). With
  `duration` now populated, this bar becomes real automatically — no code change, just verify the
  binding path exists (`ProgressFraction`). If `VideoCard` has NO progress visual at all, add a thin
  `ProgressBar Height="3" Maximum="1" Value="{Binding ProgressFraction}"` at the bottom of the card,
  collapsed when `ProgressFraction <= 0` — but ONLY if it's genuinely absent (read the file first).
- (Empty states already exist: rails collapse via `Has…`; the global `IsEmpty` message remains. The new
  stats strip hides when `HasStats` is false. No further empty-state work needed.)

**Verify** builds + gate green. **Commit** `M12: honest Home rail names (Creators / More to watch)`.

---

## Task 9 — Harness sweep + screenshot verification

### 9a. Ensure the harness Home shows real data
Read `tools/harness/Run-VisualSweep.ps1` + the `--seed-demo`/`HarnessRunner` seeding. The `home`/`search`
captures use `--folder` + `--autostart` (which triggers a scan → `ScanAndReload` → **the new backfill
runs on the real fixture mp4s**, so `duration` populates and continue-watching progress becomes real).
Confirm `--seed-demo` (or the autostart scan) leaves at least **one in-progress video** (a
`resume_position` set) so the **Continue watching** rail + its progress bar are visible in `home.png`. If
seeding sets no resume, add a single `SetResumePosition` on one seeded video in the harness seed path
(`HarnessRunner` seed-demo) so the rail renders. (Fixtures likely have no chapters → the chapter label is
legitimately absent; that's acceptable.)
> If wiring a resume into the seed is non-trivial, SKIP it and note "continue-watching progress verified
> manually" — don't block the milestone; the stats strip + rail renames are still verifiable.

### 9b. Run the sweep (pwsh 7) + subagent verdict
**Before running, enumerate top-level windows and close/avoid any stray always-on-top media window**
(the M11 sweep was polluted by an external "Webcam Streams Recorder" bleeding a timecode into the GDI
grab — see the ROADMAP decision log). Run `tools/harness/Run-VisualSweep.ps1` under `pwsh` 7 on an
unlocked composited desktop. Dispatch ONE Sonnet subagent to Read the PNGs in the reported `PNG_DIR` and
return PASS/FAIL + paths, against:
1. **`home.png` stats strip** — a personal summary line ("N of M watched · Hh Mm") near the top, with a
   small top-creators chip row when data exists. Not a separate page — it's atop the same Home.
2. **Continue-watching progress** — if an in-progress video is seeded, its `VideoCard` shows a non-empty
   progress bar (duration is now real). (If not seeded, note it.)
3. **Honest rail names** — the rails read **"Creators"** and **"More to watch"**, NOT "Recommended
   creators"/"Recommended videos".
4. **No regressions** — all other M11 shell criteria still hold (no sidebar, gear/back/active-underline
   top nav, Settings page, creator Edit-mode, PiP secondaries collapsed); no external-window timecode
   bleed (re-run if a stray window contaminated a frame).

**On FAIL** fix via the implementer loop and re-sweep. **Commit** any harness changes
`M12: harness seeds an in-progress video for the Home progress/stats sweep`.

---

## Finish (controller)
1. Final gate `dotnet test VideoShelf.slnx -c Release --nologo -v q` — 0 failures (expect ~275+ tests:
   266 baseline + new schema/duration/chapters/stats/backfill/discovery tests).
2. Final whole-branch review (fresh Sonnet) over `git diff main..HEAD`: theming-rule compliance (stats
   strip is additive panels, no WPF-UI re-templates), NO cross-thread `ObservableCollection` mutation
   (the backfill's `ConfigureAwait(false)` touches only the repo, never a UI collection; stats/cards
   assign on the UI continuation), the backfill is incremental + crash-safe (only null-duration, per-file
   try/catch, idempotent), and the libVLC probe is the only new native surface (thin, integration-only).
3. Push `feat/personal-home-stats`; open the PR; **foreground** `gh pr checks <PR#> --watch` (sleep ~20s
   first); merge `--merge --delete-branch` from the main repo root; sync main; remove the worktree.
4. **Update `ROADMAP.md`** via a **docs branch + PR** (ROADMAP flips do NOT go direct-to-main — owner
   rule): flip M12 to ✅ Merged with the PR #, a one-line summary, and an M12-shipped decision-log entry
   (durable facts: duration was a pre-existing unused column; the player-based probe gets duration +
   chapters in one pass; backfill is incremental/crash-safe and hooks into `ScanAndReload` before
   `Discovery.LoadAsync`; `ContinueWatchingCardViewModel.ProgressFraction` lit up automatically once
   duration populated; `video_chapters` table + `ChapterRecord`; `StatsRepository`; the exact
   `FullChapterDescriptions`/`TimeOffset` API used; any STOP-and-report items hit).
5. **Ping** the handoff for planning **M13 (Subtitles & play-queue)**.

## STOP-and-report triggers (don't guess)
- The pinned LibVLCSharp `MediaPlayer.FullChapterDescriptions()` shape / `ChapterDescription.TimeOffset`/
  `.Name` members differing from Task 4's assumption, or `Playing`-wait differing from `LibVlcThumbnailService`.
- The `sections.display_name` / `series.section_id` / `videos.series_id` (or `videos.duration`,
  `resume_position`, `watched`, `missing`) column names differing from the digest.
- `ContinueWatchingItem`/`ContinueWatchingCardViewModel`/`MakeContinueCard` shapes differing (esp. how
  the continue card is constructed) so the chapter label can't be threaded in cleanly.
- `VideoCard.xaml` not actually rendering `ProgressFraction` (then Task 8's add-a-ProgressBar branch).
- Any WPF-UI control needing a full `ControlTemplate` override to add the stats strip (it shouldn't).
