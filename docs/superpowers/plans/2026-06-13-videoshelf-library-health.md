# M18 — Library health & maintenance (VideoShelf)

> **Written for Sonnet execution.** If anything here does not match the actual code (a signature, a column name, a libVLC API), **STOP and report** rather than guess. This plan is the source of truth for *what* to build; the repo is the source of truth for *how the surrounding code looks* — reconcile by reading the cited file, never by inventing.
>
> **Repo:** `C:\Agent Projects\VideoShelf` · default branch `main` · solution `VideoShelf.slnx` (.NET 10 WPF).
> **`gh` is NOT on PATH:** call `& "C:\Program Files\GitHub CLI\gh.exe"`. **Direct pushes to `main` are blocked** — every change ships via a worktree branch + PR, merged `--merge` from the **main repo root** (not the worktree).
> **Test gate:** `dotnet test VideoShelf.slnx -c Release --nologo -v q` (build with `-v minimal`). Current baseline: **670 tests (303 Core + 367 App)**. The gate is *green + screenshot sweep PASS*, not a test count.
> **Commits:** author `yovanmc` + `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`. No Codex trailer.
> **No `user_version` runner — do NOT add one.** All schema changes use `CREATE TABLE IF NOT EXISTS` (full `Schema` string) + the idempotent `EnsureColumn(conn, table, column, def)` guard in `VideoShelfDb.Migrate()` (see `src/VideoShelf.Core/Storage/VideoShelfDb.cs:29-71,82-191`). The M8→M17 no-runner streak holds.

---

## Owner decisions (locked 2026-06-13, batched `AskUserQuestion` ×2)

1. **Grouping override = FULL** — split (re-route a video into a new/other series), merge (fold two series into one), and manual episode order. All **rescan-surviving** via the `grouping_overrides` table.
2. **Resolution = INCLUDE** — capture width/height, crash-safe backfill (mirrors the M12 duration backfill). Powers (a) the duplicate keeper decision, (b) a future resolution smart-view filter (M16-deferred), (c) at-a-glance quality.
3. **Duplicate detection = size + exact duration.** Flag videos sharing the **same file size in bytes AND the same duration (rounded to the nearest second)**. Surface the flag **on the creator's page** (not only a dashboard). Provide a **compare-and-play screen** (play each candidate, see size/duration/resolution) to pick the keeper.
4. **Keeper action = Recycle Bin.** When the owner picks the keeper, the non-keeper file is sent to the **Windows Recycle Bin** (recoverable) and removed from the library — behind a confirm. **This is the FIRST on-disk deletion in VideoShelf** (the rename tool was the only prior on-disk writer). Honor the standing destructive-op discipline: **verify the keeper exists & is non-zero on disk BEFORE recycling the loser; never recycle if the keeper is missing.**
5. **"Not a duplicate" dismissal is persisted** (`dismissed_duplicates` table, ordered video-id pair) so a dismissed pair never re-flags.
6. **All other cleanup (orphan/empty-creator) is DB-index-only — never touches disk** (the read-only-library invariant holds for everything except the explicit Recycle-Bin keeper action).

---

## Delivery model

M18 is large. Deliver it as **stacked PRs split at the GROUP seams below** (the M16/M17 model) — split at a group boundary, never mid-group. **Group A is the foundation; do it first.** Groups A–D are pure/Core (low-risk, land them early); E–J are App/UI. The ROADMAP flip + this plan's status change ride the **final** PR's docs commit (owner rule), OR a dedicated docs commit on the first PR — either is fine as long as ROADMAP ends at ✅ Merged with every PR # listed.

**Screenshot sweep is the only gate that proves the app launches** (unit tests never construct `MainWindow`). Per the M17 crash-on-launch lesson, run an **app-launch smoke (harness `--done-signal`) after Group E** (the first new page) and again in the final sweep — do not wait until Group J to discover a XAML resource error.

---

## Group A — Schema, file-size + scan-diff foundation (Core)

**Goal:** add the columns/tables M18 needs, capture file size during scan (cheap, no libVLC), and make scan return a diff. All idempotent, no `user_version`.

### A1. Schema additions — `src/VideoShelf.Core/Storage/VideoShelfDb.cs`

In the `Schema` string add (each `CREATE TABLE IF NOT EXISTS`):

```sql
CREATE TABLE IF NOT EXISTS dismissed_duplicates (
    video_id_a INTEGER NOT NULL REFERENCES videos(id) ON DELETE CASCADE,
    video_id_b INTEGER NOT NULL REFERENCES videos(id) ON DELETE CASCADE,
    dismissed_at TEXT NOT NULL,
    PRIMARY KEY (video_id_a, video_id_b)
);
```

In `Migrate()` after the existing `EnsureColumn` calls, add (these guard `ALTER TABLE videos ADD`):

```csharp
EnsureColumn(conn, "videos", "size_bytes", "INTEGER");   // file size from FileInfo.Length
EnsureColumn(conn, "videos", "width",      "INTEGER");   // video pixel width  (resolution probe)
EnsureColumn(conn, "videos", "height",     "INTEGER");   // video pixel height
EnsureColumn(conn, "sources", "last_scan_utc", "TEXT");  // per-source last scan (ISO8601 "o")
```

> **VERIFY FIRST:** confirm `grouping_overrides` is already declared in `Schema` with columns `id, section_id, file_path, override_base_title, override_episode_no, UNIQUE(section_id, file_path)` (the digest says it is, and `LibraryRepository.UpdateVideoPath` already repaths its `file_path`). If it is **absent**, add it (same shape). If present, **do not duplicate it.**

> **Convention:** `dismissed_duplicates` is keyed by the **ordered** id pair `(min(a,b), max(a,b))` — always insert/query with the smaller id first so `(7,12)` and `(12,7)` are the same row. The repo enforces ordering, not the schema.

### A2. Capture `size_bytes` during scan — `src/VideoShelf.Core/Scanning/ScanService.cs` + `LibraryRepository.UpsertVideo`

`UpsertVideo` currently does not take size. **Do NOT churn its signature across all callers** — add an *optional trailing* `long? sizeBytes = null` param (the nullable-trailing pattern, see ROADMAP decision log M16(3)). When non-null, write it to `videos.size_bytes`. In `ScanService.ScanSource`, compute `new FileInfo(full).Length` (wrap in try/catch → null on failure; a missing/locked file just leaves size null, backfilled later).

Add `LibraryRepository.GetVideosNeedingSize()` → ids+paths `WHERE size_bytes IS NULL AND missing=0`, and `SetSizeBytes(long videoId, long bytes)`. (A cheap filesystem-only backfill for the existing library; no libVLC. Hook it into `MainViewModel.ScanAndReload` — see G/Backfill wiring, or run it inline at scan end since it's fast.)

### A3. Scan returns a diff — `ScanService.ScanSource` → `ScanResult`

Refactor the **void** `ScanSource(string sourceRoot, string displayName)` to return:

```csharp
public sealed record ScanResult(int Added, int Updated, int Restored, int Missing);
```

- **Added** = videos whose `file_path` did not exist before this scan.
- **Restored** = videos that existed and were `missing=1` before, now found (`missing` cleared).
- **Updated** = videos that existed, were already `missing=0`, and were re-seen (touched).
- **Missing** = videos still `missing=1` after the scan (existed before, not found on disk now).

Implementation sketch (compute from the DB around the existing mark-all-missing → re-find flow):

```csharp
public ScanResult ScanSource(string sourceRoot, string displayName)
{
    var sourceId = library.UpsertSource(sourceRoot, displayName);
    // Snapshot BEFORE: file_path -> wasMissing, for this source
    var before = library.GetVideoPathStates(sourceId); // Dictionary<string,bool> path->missing
    library.MarkAllMissingForSource(sourceId);

    int added = 0, restored = 0, updated = 0;
    foreach (var section in FolderScanner.Scan(sourceRoot))
    {
        var sectionId = library.UpsertSection(sourceId, section.FolderName);
        var overrides = library.GetGroupingOverrides(sectionId);            // Group B (empty until B lands)
        var grouped = SectionGrouper.Group(section.Files.Select(f => f.FileName).ToList(), overrides);
        foreach (var series in grouped.Series)
        {
            var seriesId = library.UpsertSeries(sectionId, series.BaseTitle, series.IsStandalone);
            foreach (var episode in series.Episodes)
            {
                var full = Path.Combine(sourceRoot, section.FolderName, episode.FileName);
                long? size = TryFileSize(full);
                library.UpsertVideo(seriesId, full, episode.EpisodeNumber, Path.GetExtension(episode.FileName), size);
                library.ClearMissing(full);
                if (!before.TryGetValue(full, out var wasMissing)) added++;
                else if (wasMissing) restored++;
                else updated++;
            }
        }
    }
    int missing = library.CountMissingForSource(sourceId);
    library.SetSourceLastScanUtc(sourceId, now()); // now passed in or DateTimeOffset.UtcNow at the App boundary
    return new ScanResult(added, updated, restored, missing);
}
```

> **STOP-and-report** if `ScanService` already has callers that depend on the `void` return (grep `ScanSource(`); update them to ignore/aggregate the `ScanResult`. The `IScanCoordinator`/scan-coordinator in the App layer must bubble the aggregated `ScanResult` up to the VM (sum across sources).

New repo methods for A3: `GetVideoPathStates(long sourceId)`, `CountMissingForSource(long sourceId)`, `SetSourceLastScanUtc(long sourceId, DateTimeOffset utc)`, `GetSourceLastScanUtc(long sourceId)`.

**Tests (Core):** `ScanDiffTests` — seed a temp dir, scan (all Added); add a file + delete a file, re-scan (1 Added, 1 Missing); restore the deleted file, re-scan (1 Restored). `size_bytes` populated. Use the existing temp-DB fixture pattern.

---

## Group B — Grouping overrides wired into the pipeline (Core)

**Goal:** make split/merge/manual-order real by having the grouping pipeline consult `grouping_overrides`. Pure + unit-tested.

### B1. `SectionGrouper.Group` overload that applies overrides — `src/VideoShelf.Core/Naming/SectionGrouper.cs`

Current: `Group(IReadOnlyList<string> fileNames)` → parses each via `TitleParser`, groups by base title, orders by episode. Add an overload:

```csharp
public sealed record GroupingOverride(string FilePath, string? OverrideBaseTitle, int? OverrideEpisodeNo);

public static GroupedSection Group(
    IReadOnlyList<string> fileNames,
    IReadOnlyDictionary<string, GroupingOverride> overridesByFileName);
```

For each file: parse normally, then if an override exists for that filename, **replace** `BaseTitle` with `OverrideBaseTitle` (when non-null) and/or `EpisodeNumber` with `OverrideEpisodeNo` (when non-null) **before** grouping/sorting. Keep the existing no-arg overload delegating to the new one with an empty dictionary (so all current callers compile).

> **Keying note:** `grouping_overrides` is keyed by **`file_path`** (full path); `SectionGrouper` works on **bare file names**. The repo method `GetGroupingOverrides(sectionId)` must return a dictionary keyed by the **bare file name** (`Path.GetFileName(file_path)`) so the grouper can look it up — OR pass full paths into the grouper. Choose the bare-filename keying (less churn in `SectionGrouper`). Document the choice in a comment.

**Semantics of the three operations** (all expressed as override rows — no new table):
- **Split** a video out of series X into new series "Y": set `override_base_title='Y'` for that file (and optionally `override_episode_no`).
- **Merge** series B into series A: set `override_base_title='<A.base_title>'` for every file currently in B.
- **Manual order:** set `override_episode_no=N` for the file.

### B2. Repository — `src/VideoShelf.Core/Storage/LibraryRepository.cs`

```csharp
IReadOnlyDictionary<string, GroupingOverride> GetGroupingOverrides(long sectionId); // keyed by bare file name
void SetGroupingOverride(long sectionId, string filePath, string? baseTitle, int? episodeNo); // UPSERT on UNIQUE(section_id,file_path)
void ClearGroupingOverride(long sectionId, string filePath);
```

`SetGroupingOverride` uses `INSERT ... ON CONFLICT(section_id, file_path) DO UPDATE` (`@`-prefixed params per the M16 TagRepository lesson — **NOT `$`**). Setting both `baseTitle` and `episodeNo` to null is equivalent to clear (or callers use `ClearGroupingOverride`).

> **Rescan-survival is automatic:** overrides are keyed by `(section_id, file_path)`, applied on every `ScanService.ScanSource` (A3 already passes `overrides` into `SectionGrouper.Group`). `UpdateVideoPath` already repaths `grouping_overrides.file_path` on rename (verified in the digest), so a relinked/renamed file keeps its override.

**Tests (Core):** `GroupingOverrideTests` — (a) split: two files of one series, override one to a new title → two series; (b) merge: two series, override one's files to the other's title → one series with merged episodes ordered by override/derived episode; (c) manual order: override episode numbers reorder; (d) survival: build overrides, re-run `SectionGrouper.Group` with the same dict → stable.

---

## Group C — Resolution probe + crash-safe backfill (App + Core)

**Goal:** capture width/height per video; backfill the existing library incrementally and crash-safely (mirrors `MediaBackfillService`).

### C1. Extend the probe result — `src/VideoShelf.App/Services/IMediaProbe.cs`

```csharp
public sealed record MediaProbeResult(
    double? DurationSeconds,
    IReadOnlyList<ChapterRecord> Chapters,
    int? Width,
    int? Height);
```

### C2. Capture resolution in `LibVlcMediaProbe.ProbeAsync` — `src/VideoShelf.App/Services/LibVlcMediaProbe.cs`

After the player reaches `Playing` (where duration/chapters are already read), read the video pixel size.

> **VERIFY THE libVLC API FIRST (mirror how M12 found `FullChapterDescriptions`):** in pinned **LibVLCSharp 3.9.7.1** the runtime video size is most reliably obtained via **`mediaPlayer.Size(0, ref uint px, ref uint py)`** (returns `false` if unavailable). Fallback: enumerate `mediaPlayer.Media.Tracks` for a `TrackType.Video` track and read `.Data.Video.Width/.Height`. **Reflection-confirm the actual member names before coding** (the M12/M13 plans did exactly this for chapters/slaves). If neither is available while playing, return `Width=null,Height=null` (fail-safe — never block the probe). Round/cast to `int`.

Existing callers of `ProbeAsync` (the `MediaBackfillService`) now also receive Width/Height → persist them in the **same** probe pass (no second play per file): in `MediaBackfillService.BackfillAsync`, after `SetDuration`, also call `library.SetResolution(v.Id, r.Width, r.Height)` when both are non-null.

### C3. Resolution backfill for the **existing** library — Core repo + new service

`MediaBackfillService` only probes `WHERE duration IS NULL`, so videos already given a duration in M12 won't get resolution from it. Add:

- `LibraryRepository.GetVideosNeedingResolution()` → ids+paths `WHERE width IS NULL AND missing=0`.
- `LibraryRepository.SetResolution(long videoId, int width, int height)`.
- New `src/VideoShelf.App/Services/ResolutionBackfillService.cs` — a **carbon copy of `MediaBackfillService`** (incremental, per-file try/catch, `OperationCanceledException` rethrow, `ConfigureAwait(false)` — it touches only the repo, no UI collection) iterating `GetVideosNeedingResolution()`, probing, and calling `SetResolution` when Width/Height present.

### C4. Wire the backfill — `MainViewModel.ScanAndReload`

After the existing `MediaBackfillService` backfill (and before/after the size backfill A2), run `ResolutionBackfillService.BackfillAsync`. Add it as a ctor dep on `MainViewModel` (one new param) **OR** prefer the nullable-trailing pattern (`ResolutionBackfillService? = null`) to avoid touching the test factory — choose nullable-trailing and guard the call. Update `MainViewModelTestFactory` only if you make it required.

**Tests (App):** `ResolutionBackfillServiceTests` with a `FakeMediaProbe` returning fixed Width/Height → asserts `SetResolution` called for needing-resolution videos, skipped when null, and cancellation rethrows. Extend `FakeMediaProbe` to carry Width/Height.

---

## Group D — Duplicate detection + dashboard data (Core)

**Goal:** the read queries that power the dashboard and the duplicate compare screen. Pure SQL, unit-tested.

### D1. Duplicate groups — `LibraryRepository` (or a new `MaintenanceRepository`)

```csharp
public sealed record DuplicateVideo(long Id, long SectionId, string CreatorName, string SeriesTitle,
                                    string FilePath, long? SizeBytes, double? DurationSeconds, int? Width, int? Height);
public sealed record DuplicateGroup(long SizeBytes, int DurationRoundedSeconds, IReadOnlyList<DuplicateVideo> Videos);

IReadOnlyList<DuplicateGroup> GetDuplicateGroups();                 // all duplicate candidates, library-wide
IReadOnlyList<DuplicateGroup> GetDuplicateGroupsForSection(long sectionId); // creator-page scoped
```

**Signal (locked):** two videos are candidates iff `missing=0` AND `size_bytes` equal AND `CAST(ROUND(duration) AS INTEGER)` equal. Group by `(size_bytes, CAST(ROUND(duration) AS INTEGER))` having `COUNT(*) > 1`. **Exclude** any pair present in `dismissed_duplicates` — a group splits/drops once every cross-pair in it is dismissed (simplest correct rule: when building groups, drop a video from a group if it is dismissed against *every other* member; if a group falls below 2 members it disappears). Implement the dismissal filtering in C#/LINQ over the raw grouped rows (clearer than SQL), unit-tested.

Join for `CreatorName`/`SeriesTitle`: `videos → series(series_id) → sections(section_id)`.

### D2. Dismissals — repo

```csharp
void DismissDuplicatePair(long videoIdA, long videoIdB, DateTimeOffset now); // store ordered (min,max)
bool IsDuplicatePairDismissed(long videoIdA, long videoIdB);
IReadOnlyList<(long A, long B)> GetDismissedPairs();
```

### D3. Maintenance summary — repo

```csharp
public sealed record MaintenanceSummary(
    int MissingCount, int OrphanSeriesCount, int EmptyCreatorCount,
    int SingleFileSeriesCount, int DuplicateGroupCount, long DbSizeBytes);

MaintenanceSummary GetMaintenanceSummary();
```

- Missing: `COUNT(*) FROM videos WHERE missing=1`.
- Orphan series: series with zero `missing=0` videos.
- Empty creator (section): section with zero `missing=0` videos.
- Single-file series: series with exactly one `missing=0` video.
- Duplicate group count: from D1.
- DB size: `SELECT page_count * page_size` via `PRAGMA page_count; PRAGMA page_size;` (two pragmas; multiply in C#).

### D4. Missing/orphan lists — repo

```csharp
IReadOnlyList<MissingVideo> GetMissingVideos();    // id, file_path, creator, series — for the relink triage list
IReadOnlyList<OrphanEntry> GetOrphanSeries();       // series id+title+creator with zero playable
IReadOnlyList<OrphanEntry> GetEmptyCreators();      // sections with zero playable
void DeleteSeriesIndex(long seriesId);              // DB-index-only removal (CASCADE deletes its videos rows); NEVER touches disk
void DeleteSectionIndex(long sectionId);            // DB-index-only removal; NEVER touches disk
```

> **Safety:** `DeleteSeriesIndex`/`DeleteSectionIndex` remove **DB rows only** — they never call the filesystem. If files reappear on disk, the next scan re-adds them. This preserves the read-only-library invariant. (Recoverable by definition.)

**Tests (Core):** `DuplicateDetectionTests` (size+duration grouping; dismissal removes a pair/group; section-scoped variant), `MaintenanceSummaryTests` (each count + DB size > 0), `OrphanCleanupTests` (delete-index removes the series/section + its video rows, leaves other data, no FS calls — assert via a temp dir untouched).

---

## Group E — Maintenance dashboard page (App)

**Goal:** a dedicated page reached from the unified **Library** menu (M16 pattern), showing health at a glance + drill-ins.

- New `AppView.Maintenance` enum value (`MainViewModel.cs:13`).
- New `MaintenanceViewModel` (loads `GetMaintenanceSummary`, per-source rows with last-scan + a per-source **Rescan** command, last `ScanResult` text) + `MaintenanceView.xaml`.
- Add a **Library**-menu entry `MaintenanceMenuItem` → `ShowMaintenanceCommand` (a `Menu`/`MenuItem` inherits the window DataContext — M16(6)). Active-underline via the existing `EnumSetToVis` set.
- Gate the host by `DataContext.CurrentView == Maintenance` (the M6 silent-fallback trap — do **not** bind to `CurrentView` without the `DataContext.` qualifier, the M6 Critical).
- Tiles: Missing (N) · Orphans (N) · Empty creators (N) · Single-file series (N) · Duplicates (N) · DB size (MB). Each tile that has a drill-in navigates to its sub-view/expander.
- Per-source card: root path · last scan (relative) · video count · **Rescan this source** button.
- Scan-diff banner: "Last scan: added 12, updated 3, restored 1, missing 1" (Group I feeds this).

**Use only owned/verified resources** (per the M17 crash-on-launch lesson): `DesignTokens.xaml` brushes are **direct** `SolidColorBrush` (`TextPrimaryBrush #FFFFFFFF`, `TextSecondaryBrush #C5FFFFFF`, etc.). **Never** alias a WPF-UI theme brush via nested `<StaticResource ResourceKey=…>` consumed with `{StaticResource}`. Additive only — never retemplate a `ui:*` control.

> **APP-LAUNCH SMOKE after this group:** run the harness with `--done-signal` for `--view maintenance` (and one existing view) and confirm it writes `OK view=…`. This is the M17 lesson — catch a missing-resource/XAML error here, not in Group J.

**Tests (App):** `MaintenanceViewModelTests` (summary maps to properties; per-source rows; rescan command calls the coordinator). View is integration-only (sweep).

---

## Group F — Missing-file triage & relink + orphan cleanup (App)

**Goal:** turn silent dimming into actionable relink, and let the owner prune dead index entries (DB-only).

- **Missing list** (sub-view of Maintenance): each missing video → path, creator/series, **Relink…** + **Auto-find** + (for orphan series/empty creators) **Remove from library**.
- **Relink…** opens a file picker. Add `IVideoFilePicker` + `VideoFilePicker` mirroring the existing `ISubtitleFilePicker`/`SubtitleFilePicker` (interface+impl in ONE file; `FakeVideoFilePicker { NextResult }` for tests). On pick → `LibraryRepository.UpdateVideoPath(id, oldPath, newPath)` (watch-state/tags/chapters survive — id-based; the override file_path repaths too). Clear `missing` for the new path.
- **Auto-find** (nice-to-have, keep simple): scan the video's source root for a file with **matching `size_bytes` (and duration if known)**; if exactly one match, offer one-click relink; if 0 or >1, fall back to the manual picker. Pure matcher in Core (`RelinkMatcher.FindCandidate(missing, candidates)`), unit-tested; the App walks the source tree.
- **Orphan/empty-creator cleanup:** list from D4; **Remove from library** → `DeleteSeriesIndex`/`DeleteSectionIndex` behind a confirm dialog. Copy says "Removes from VideoShelf only — your files are not touched."

**Tests:** `RelinkMatcherTests` (exact size match → candidate; ambiguous → none), `MissingTriageViewModelTests` (relink calls `UpdateVideoPath`; remove calls the index-delete; uses fakes).

---

## Group G — Duplicate compare, resolve & dismiss (App) — the keeper flow

**Goal:** the owner's described flow — on a creator's page see "Possible duplicates (N)", open a compare screen, **play each** candidate, pick the keeper, recycle the rest, or mark "not a duplicate".

### G1. Recycle-bin service (the first on-disk deletion) — abstraction + concrete

- `src/VideoShelf.App/Services/IRecycleBinService.cs`: `bool SendToRecycleBin(string filePath);`
- Concrete `RecycleBinService` using Windows shell **`SHFileOperation` P/Invoke** with `FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT` (sends to Recycle Bin, recoverable; double-null-terminated `pFrom`). **No new NuGet** (avoids the `package` job's media-tool denylist concerns; P/Invoke is dependency-free). `FakeRecycleBinService { List<string> Recycled; bool NextResult=true }` for tests.

> **STOP-and-report / VERIFY:** confirm the `SHFileOperation` struct marshalling on .NET 10 x64 (pack, `FILEOP_FLAGS` width, double-null `pFrom`). If anything is uncertain, the safe fallback is `Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin)` (the `Microsoft.VisualBasic` assembly ships in the .NET runtime, **not** a media tool — but confirm the `package` no-media-tools denylist is unaffected). Pick whichever you can verify; keep it behind `IRecycleBinService` so the choice is swappable and the VM stays testable.

### G2. Keeper resolution logic — `DuplicateResolveViewModel` (plain VM, unit-tested)

```csharp
// For one DuplicateGroup:
//  - expose each DuplicateVideo with Size, Duration, Resolution (e.g. "1920×1080"), a Play command, a "Keep this" command.
//  - KeepCommand(keeperId):
//        1) SAFETY GATE: verify the keeper's file exists on disk AND length > 0 (re-check, do not trust stale DB).
//           If the keeper is missing/zero → ABORT, surface an error, recycle NOTHING. (user's verify-before-destroy rule)
//        2) For every OTHER video in the group: IRecycleBinService.SendToRecycleBin(path); on success → library.DeleteVideoIndexById(id).
//        3) Raise a Resolved event so the creator page / dashboard refresh.
//  - NotADuplicateCommand: for each cross-pair in the group, library.DismissDuplicatePair(a,b,now); refresh.
```

- `LibraryRepository.DeleteVideoIndexById(long videoId)` — remove the single `videos` row (CASCADE cleans `video_tags`/`video_chapters`/`watch_events`/`video_art`). DB-only.
- Confirm dialog before recycling (count + filenames). The Play command routes through the existing `MainViewModel.OpenPlayer`/single-play path so the owner can eyeball each clip.

### G3. Surface on the creator page — `SectionDetailViewModel`

- Add `PossibleDuplicates` (from `GetDuplicateGroupsForSection(sectionId)`), `HasDuplicates`, and a command to open the compare screen for a group. Render a small banner/affordance on the creator page ("Possible duplicates (N) — review"). Use the **nullable-trailing ctor param** pattern for the new repo/service deps on `SectionDetailViewModel` so the ~test sites compile unchanged (M16(3)).
- Also reachable from the Maintenance dashboard Duplicates tile (library-wide list).

**Tests (App):** `DuplicateResolveViewModelTests` — keeper-exists → others recycled + index-deleted + Resolved raised; **keeper-missing → nothing recycled, error surfaced** (the safety gate); NotADuplicate → dismissals stored; uses `FakeRecycleBinService` + a temp DB. `SectionDetailDuplicatesTests` — section-scoped group surfaces; dismissal removes it.

---

## Group H — Manual grouping override UI (App) — split / merge / reorder

**Goal:** expose Group B's override operations on the creator page (Edit mode), writing override rows then regrouping.

- On the creator page in **Edit mode** (`IsEditing`, M11), add per-series / per-episode affordances:
  - **Move episode** (manual order): up/down or set-number → `SetGroupingOverride(sectionId, filePath, null, newEpisodeNo)`.
  - **Move to series…** (split or re-route): pick/enter a target series title → `SetGroupingOverride(sectionId, filePath, targetTitle, null)`.
  - **Merge into…** (series-level): pick another series in the creator → set every file in this series' `override_base_title` to the target's base title.
  - **Reset grouping** (per file/series): `ClearGroupingOverride`.
- After any override change: re-run grouping for that section and reload the creator page. Simplest correct approach: call the scan-coordinator's per-source/section regroup (overrides are applied by `ScanService` via Group A/B) OR a lighter `LibraryRepository`-side regroup that re-derives series from current `videos` + overrides without touching disk. **STOP-and-report** if a disk rescan is too heavy for a snappy UI — in that case add a Core `RegroupSection(sectionId)` that re-buckets existing `videos` rows by `SectionGrouper.Group(filenames, overrides)` and updates `series_id`/`episode_no` in a transaction (no FS). Prefer `RegroupSection` for responsiveness; it must be idempotent and rescan-consistent (a later disk scan must produce the same grouping).

> **This is the riskiest UX in M18.** Keep the VM logic pure and unit-tested (`GroupingEditViewModel` operating on repo methods + a fake), and keep the XAML additive. If the in-place-accordion layout makes the affordances awkward, **STOP-and-report** — do not restructure the hero/accordion layout without owner sign-off (that's the deferred E2 layout pass from M17(5)).

**Tests (App/Core):** `RegroupSectionTests` (split moves a video to a new series; merge folds two; manual episode_no persists; re-running is stable), `GroupingEditViewModelTests` (each command writes the right override + triggers regroup).

---

## Group I — Scan-diff feedback surfacing (App)

**Goal:** show the `ScanResult` after any scan.

- The scan-coordinator returns the aggregated `ScanResult`; `MainViewModel`/`SettingsViewModel`/`MaintenanceViewModel` expose `LastScanSummaryText` ("Added 12 · updated 3 · restored 1 · missing 1"). Show it on Settings (next to last-scanned) and on the Maintenance dashboard banner. Persist the last result string in `settings` (key `last_scan_summary`) so it survives a restart.
- Per-source last-scan times come from `sources.last_scan_utc` (Group A) — show on each source row.

**Tests (App):** `ScanSummaryTests` — coordinator aggregates multi-source `ScanResult`; VM formats the text; persisted/restored.

---

## Group J — Harness, screenshot sweep & ctor consolidation (App)

- Extend `HarnessRunner` with `--view maintenance` (and any new sub-views you made routable). Extend `SeedDemoAsync` to seed: a **missing** video (insert a row with a bogus path + `missing=1`), a **duplicate pair** (two videos with identical `size_bytes` + `duration`, different paths/names, same creator), an **orphan series** (series with only a missing video), and **resolution** values so the compare screen renders dimensions. Idempotent seeding.
- Consolidate any deferred `MainViewModel` ctor fan-out in this final task (batch the new VM params; update `MainViewModelTestFactory` once).
- **Screenshot sweep** (pwsh-7, unlocked desktop, **close stray always-on-top media windows** — the recurring "Webcam Streams Recorder"/League bleed class): capture Maintenance, the missing-triage list, the duplicate compare screen, and the creator page with the duplicates banner + Edit-mode grouping affordances. **A Sonnet subagent views the PNGs and returns a TEXT verdict** (PASS/FAIL + observations + absolute paths) — never load PNGs into the controller context (ROADMAP token rule; [[feedback-screenshot-verify-in-subagent]]). Tall views need a scrolled multi-shot (1280×860).

**Acceptance for the sweep:** Maintenance tiles render with seeded counts; the duplicate compare screen shows two clips with size/duration/**resolution** + Play + Keep; the creator page shows "Possible duplicates (N)"; Edit-mode shows move/merge affordances; no nav-overlap, no transport-bar bleed, all owned brushes resolve (app launches on every view).

---

## Cross-cutting STOP-and-report flags (collected)

1. `grouping_overrides` table shape — verify it's already declared before adding (A1).
2. `ScanSource` callers depending on the `void` return (A3).
3. libVLC resolution API in 3.9.7.1 — reflection-confirm `Size(...)` vs `Media.Tracks` before coding (C2).
4. `SHFileOperation` marshalling vs the `Microsoft.VisualBasic` fallback for the Recycle Bin (G1).
5. The keeper safety gate must abort if the keeper is missing/zero — recycle nothing (G2).
6. `RegroupSection` vs a full disk rescan for snappy override edits; do NOT restructure the creator-page layout without owner sign-off (H).
7. Any task ballooning past a clean group seam → split into its own stacked PR; never split mid-group.

## Out of scope (explicit)
- No auto-delete of anything except the **explicit, confirmed** duplicate-keeper Recycle-Bin action.
- No online metadata / no ffmpeg/HandBrake (the no-media-tools `package` job must stay green).
- Resolution **smart-view filter UI** is NOT built here — M18 only *captures* resolution; the filter is a later smart-view add (the M16-deferred axis is now unblocked).
- No `user_version` migration runner.
```

