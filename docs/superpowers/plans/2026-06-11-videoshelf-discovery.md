# VideoShelf Milestone 4 — Discovery + Tags Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **Written for Sonnet execution. If something in the codebase doesn't match what this plan describes (a method body, a namespace, a signature), STOP and report rather than guess.** This plan was written from a digest, not from reading every file body; a few tasks explicitly tell you to read a neighboring file first and mirror it.

**Goal:** Add a Discovery experience (Home landing with Continue-watching, Recently-added, Recently-watched, For-you and Pick-a-tag rails) plus section-level tagging via a dedicated section-detail view, all on the existing read-only VideoShelf stack.

**Architecture:** All data/query/scoring logic lands in `VideoShelf.Core` (a new `TagRepository`, a pure `DiscoveryScoring` helper, a `DiscoveryRepository` of rail queries) and is unit-tested against in-memory SQLite. The App layer adds plain MVVM ViewModels (Discovery rails, card/chip VMs, a `SectionDetailViewModel` tag editor) unit-tested with fakes, plus thin XAML views and a top-level Home/Browse navigation switch on `MainViewModel`. Views are integration-only (screenshot-verified in Milestone 6), consistent with the established testability pattern.

**Tech Stack:** .NET 10, WPF + WPF-UI (Fluent dark Mica), CommunityToolkit.Mvvm (`ObservableObject`/`RelayCommand`), Microsoft.Data.Sqlite, xUnit + Shouldly.

---

## Conventions for every task (read once)

- **Match the neighbor.** When adding a type to an existing folder, copy the file-scoped-namespace style, primary-constructor style, `using` directives, and naming of the nearest existing file. If a type reference (e.g. `SeriesSummary`, `EpisodeView`) doesn't resolve, find its declaration and add the `using` — **do not invent a namespace.**
- **Build/test gate (full):** `dotnet test VideoShelf.slnx -c Release --nologo -v q`
- **Single-class test run (project-path-agnostic):** `dotnet test VideoShelf.slnx -c Release --nologo -v q --filter "FullyQualifiedName~<ClassName>"`
- **Baseline before you start:** 140 tests (60 Core + 80 App), 0 failures. Every task only adds tests; the full suite must stay green.
- **Cross-thread gotcha (CRITICAL — caused a Phase-2 Critical bug):** never put `ConfigureAwait(false)` on an async chain that ends by mutating a UI-bound `ObservableCollection`. Do heavy work inside `Task.Run`, but let the continuation resume on the captured UI `SynchronizationContext`. Discovery VMs mutate `ObservableCollection`s, so this applies to every VM task.
- **Theming rule:** never override/re-base a WPF-UI themed control's `Style`/`ControlTemplate` for cosmetics — additive (`Opacity`/`RenderTransform`/margins) only.
- **Commits:** plain `git commit` (git author is `yovanmc` from global config — do NOT override `user.email`). End every commit message with the trailer:
  ```
  Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
  ```
- **Worktree/branch:** work on branch `feat/discovery` in a worktree under `.worktrees/` (the execution skill sets this up). `gh` is at `& "C:\Program Files\GitHub CLI\gh.exe"`; merge from the **main repo root**, not the worktree.

---

## File structure (what this milestone creates / modifies)

**Core (`src/VideoShelf.Core/`)** — match its actual folder names; paths below assume `Storage/` and a new `Discovery/`:
- Create `Storage/TagRepository.cs` — section_tags CRUD + autocomplete + tag counts.
- Create `Discovery/DiscoveryModels.cs` — `ContinueWatchingItem`, `RecencyItem`, `SectionSuggestion`, `WatchedTag`.
- Create `Discovery/DiscoveryScoring.cs` — pure recency-decay + tag-affinity + section-scoring helpers.
- Create `Discovery/DiscoveryRepository.cs` — the rail queries (depends on `LibraryRepository` + `TagRepository`).
- Modify `Storage/VideoShelfDb.cs` — guarded migration adding `resume_updated_at TEXT` to `videos`.
- Modify `Storage/LibraryRepository.cs` — `SetResumePosition`/`ClearResumePosition` maintain `resume_updated_at`.
- Modify `Storage/WatchRepository.cs` — `SetWatched(true)` nulls `resume_updated_at` alongside `resume_position`.

**App (`src/VideoShelf.App/`)** — match its actual folders (`ViewModels/`, `Views/`, DI in `ServiceCollectionExtensions.cs`):
- Create `ViewModels/Discovery/ContinueWatchingCardViewModel.cs`, `RecencyCardViewModel.cs`, `SectionCardViewModel.cs`, `TagChipViewModel.cs`.
- Create `ViewModels/Discovery/DiscoveryViewModel.cs`.
- Create `ViewModels/SectionDetailViewModel.cs`.
- Modify `ViewModels/MainViewModel.cs` — `AppView` switch + `Discovery`/`SectionDetail` hosting + wiring.
- Modify `ServiceCollectionExtensions.cs` — register `TagRepository`, `DiscoveryRepository`, `DiscoveryViewModel`, `SectionDetailViewModel`.
- Create `Views/DiscoveryView.xaml` (+ `.cs`), `Views/SectionDetailView.xaml` (+ `.cs`); modify `MainWindow.xaml` for the Home/Browse nav.

**Tests** — `tests/VideoShelf.Core.Tests/` and `tests/VideoShelf.App.Tests/` (mirror existing folder layout & `TestSupport`).

---

## Task 1: TagRepository (Core)

**Files:**
- Create: `src/VideoShelf.Core/Storage/TagRepository.cs`
- Test: `tests/VideoShelf.Core.Tests/Storage/TagRepositoryTests.cs`

The `section_tags` table already exists (`PRIMARY KEY(section_id, tag)`). This adds the repo. Tags are normalized: trimmed, internal whitespace collapsed to single spaces, lower-cased; empty/whitespace-only tags are ignored.

- [ ] **Step 1: Write the failing test**

Open an existing Core storage test (e.g. `Storage/WatchRepositoryTests.cs`) to copy the exact `TempDb`/seeding helpers and namespace. Then create `TagRepositoryTests.cs`:

```csharp
using Shouldly;
using VideoShelf.Core.Storage; // adjust to match WatchRepository's namespace
using VideoShelf.Core.Tests.TestSupport; // adjust to match the TempDb helper's namespace
using Xunit;

namespace VideoShelf.Core.Tests.Storage;

public sealed class TagRepositoryTests
{
    private static (TempDb db, LibraryRepository lib, TagRepository tags, long sectionId) Seed()
    {
        var db = new TempDb();                       // mirror however other tests construct it
        var lib = new LibraryRepository(db.Db);      // mirror their accessor for VideoShelfDb
        var sourceId = lib.UpsertSource(@"C:\media", "Media");
        var sectionId = lib.UpsertSection(sourceId, "Creator A");
        var tags = new TagRepository(db.Db);
        return (db, lib, tags, sectionId);
    }

    [Fact]
    public void AddTag_then_GetTags_returns_it()
    {
        var (db, _, tags, sectionId) = Seed();
        using var _d = db;
        tags.AddTag(sectionId, "Comedy");
        tags.GetTags(sectionId).ShouldBe(new[] { "comedy" });
    }

    [Fact]
    public void AddTag_normalizes_and_dedupes()
    {
        var (db, _, tags, sectionId) = Seed();
        using var _d = db;
        tags.AddTag(sectionId, "  Sci   Fi  ");
        tags.AddTag(sectionId, "sci fi");      // duplicate after normalization
        tags.AddTag(sectionId, "   ");          // ignored
        tags.GetTags(sectionId).ShouldBe(new[] { "sci fi" });
    }

    [Fact]
    public void RemoveTag_removes_only_that_tag()
    {
        var (db, _, tags, sectionId) = Seed();
        using var _d = db;
        tags.AddTag(sectionId, "comedy");
        tags.AddTag(sectionId, "drama");
        tags.RemoveTag(sectionId, "Comedy"); // case-insensitive
        tags.GetTags(sectionId).ShouldBe(new[] { "drama" });
    }

    [Fact]
    public void SetTags_replaces_all_and_orders_alphabetically()
    {
        var (db, _, tags, sectionId) = Seed();
        using var _d = db;
        tags.AddTag(sectionId, "zeta");
        tags.SetTags(sectionId, new[] { "Beta", "alpha", "beta" });
        tags.GetTags(sectionId).ShouldBe(new[] { "alpha", "beta" });
    }

    [Fact]
    public void GetAllTags_returns_distinct_sorted_across_sections()
    {
        var (db, lib, tags, sectionId) = Seed();
        using var _d = db;
        var section2 = lib.UpsertSection(lib.GetSources()[0].Id, "Creator B");
        tags.AddTag(sectionId, "comedy");
        tags.AddTag(section2, "comedy");
        tags.AddTag(section2, "action");
        tags.GetAllTags().ShouldBe(new[] { "action", "comedy" });
    }

    [Fact]
    public void GetTagCounts_counts_sections_per_tag()
    {
        var (db, lib, tags, sectionId) = Seed();
        using var _d = db;
        var section2 = lib.UpsertSection(lib.GetSources()[0].Id, "Creator B");
        tags.AddTag(sectionId, "comedy");
        tags.AddTag(section2, "comedy");
        tags.AddTag(section2, "action");
        var counts = tags.GetTagCounts();
        counts.ShouldContain(new TagCount("comedy", 2));
        counts.ShouldContain(new TagCount("action", 1));
    }
}
```

> If `TempDb`/`LibraryRepository` construction differs from the snippet, mirror exactly what the other Core storage tests do and adjust the `Seed()` helper. Do not change production code to fit the test scaffold.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test VideoShelf.slnx -c Release --nologo -v q --filter "FullyQualifiedName~TagRepositoryTests"`
Expected: FAIL — `TagRepository` / `TagCount` not defined.

- [ ] **Step 3: Write the implementation**

Create `src/VideoShelf.Core/Storage/TagRepository.cs` (match the namespace + primary-constructor style of `LibraryRepository.cs`):

```csharp
using Microsoft.Data.Sqlite;

namespace VideoShelf.Core.Storage; // MATCH LibraryRepository's namespace

public sealed record TagCount(string Tag, int SectionCount);

public sealed class TagRepository(VideoShelfDb db)
{
    public static string Normalize(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return string.Empty;
        var parts = tag.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts).ToLowerInvariant();
    }

    public IReadOnlyList<string> GetTags(long sectionId)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT tag FROM section_tags WHERE section_id = @s ORDER BY tag;";
        cmd.Parameters.AddWithValue("@s", sectionId);
        var result = new List<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) result.Add(r.GetString(0));
        return result;
    }

    public void AddTag(long sectionId, string tag)
    {
        var norm = Normalize(tag);
        if (norm.Length == 0) return;
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO section_tags (section_id, tag) VALUES (@s, @t);";
        cmd.Parameters.AddWithValue("@s", sectionId);
        cmd.Parameters.AddWithValue("@t", norm);
        cmd.ExecuteNonQuery();
    }

    public void RemoveTag(long sectionId, string tag)
    {
        var norm = Normalize(tag);
        if (norm.Length == 0) return;
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM section_tags WHERE section_id = @s AND tag = @t;";
        cmd.Parameters.AddWithValue("@s", sectionId);
        cmd.Parameters.AddWithValue("@t", norm);
        cmd.ExecuteNonQuery();
    }

    public void SetTags(long sectionId, IEnumerable<string> tags)
    {
        var normalized = tags.Select(Normalize).Where(t => t.Length > 0).Distinct().ToList();
        using var conn = db.Open();
        using var tx = conn.BeginTransaction();
        using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM section_tags WHERE section_id = @s;";
            del.Parameters.AddWithValue("@s", sectionId);
            del.ExecuteNonQuery();
        }
        foreach (var t in normalized)
        {
            using var ins = conn.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = "INSERT OR IGNORE INTO section_tags (section_id, tag) VALUES (@s, @t);";
            ins.Parameters.AddWithValue("@s", sectionId);
            ins.Parameters.AddWithValue("@t", t);
            ins.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public IReadOnlyList<string> GetAllTags()
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT tag FROM section_tags ORDER BY tag;";
        var result = new List<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) result.Add(r.GetString(0));
        return result;
    }

    public IReadOnlyList<TagCount> GetTagCounts()
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT tag, COUNT(DISTINCT section_id) FROM section_tags GROUP BY tag ORDER BY tag;";
        var result = new List<TagCount>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) result.Add(new TagCount(r.GetString(0), r.GetInt32(1)));
        return result;
    }
}
```

> `db.Open()` returns an open connection per the digest. If `VideoShelfDb` instead exposes a single shared connection (check `LibraryRepository`'s pattern), mirror that exactly instead of opening/closing per call.

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test VideoShelf.slnx -c Release --nologo -v q --filter "FullyQualifiedName~TagRepositoryTests"`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```
git add src/VideoShelf.Core/Storage/TagRepository.cs tests/VideoShelf.Core.Tests/Storage/TagRepositoryTests.cs
git commit -m "feat(core): add TagRepository for section tags

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 2: `resume_updated_at` column for recency ordering (Core)

**Why:** Continue-watching must be ordered by *most-recently-resumed*. `resume_position` exists but has no timestamp, and `watch_events` only records full-watched (which clears resume). Add a `resume_updated_at TEXT` column to `videos`, maintained whenever resume is set/cleared.

**Files:**
- Modify: `src/VideoShelf.Core/Storage/VideoShelfDb.cs` (the `Migrate()` method)
- Modify: `src/VideoShelf.Core/Storage/LibraryRepository.cs` (`SetResumePosition`, `ClearResumePosition`)
- Modify: `src/VideoShelf.Core/Storage/WatchRepository.cs` (`SetWatched`)
- Test: `tests/VideoShelf.Core.Tests/Storage/ResumeUpdatedAtTests.cs`

- [ ] **Step 1: Read first, then write the failing test**

Read `Storage/VideoShelfDb.cs` `Migrate()` and locate the guarded `ALTER TABLE videos ADD COLUMN added_at ...` block (added in Phase 2). You will mirror it. Read `LibraryRepository.SetResumePosition`/`ClearResumePosition` and `WatchRepository.SetWatched` to see their exact current SQL. **If you cannot find a guarded-ALTER pattern for `added_at`, STOP and report** — the migration style is a hard prerequisite.

Create `ResumeUpdatedAtTests.cs`:

```csharp
using Shouldly;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;
using Xunit;

namespace VideoShelf.Core.Tests.Storage;

public sealed class ResumeUpdatedAtTests
{
    private static (TempDb db, LibraryRepository lib, WatchRepository watch, long videoId) Seed()
    {
        var db = new TempDb();
        var lib = new LibraryRepository(db.Db);
        var src = lib.UpsertSource(@"C:\m", "M");
        var sec = lib.UpsertSection(src, "S");
        var ser = lib.UpsertSeries(sec, "Show", isStandalone: false);
        var vid = lib.UpsertVideo(ser, @"C:\m\S\Show\e01.mkv", 1, "mkv");
        var watch = new WatchRepository(db.Db);
        return (db, lib, watch, vid);
    }

    [Fact]
    public void SetResumePosition_sets_resume_updated_at()
    {
        var (db, lib, _, vid) = Seed();
        using var _d = db;
        lib.SetResumePosition(vid, 42.0);
        ReadResumeUpdatedAt(db, vid).ShouldNotBeNull();
    }

    [Fact]
    public void ClearResumePosition_nulls_resume_updated_at()
    {
        var (db, lib, _, vid) = Seed();
        using var _d = db;
        lib.SetResumePosition(vid, 42.0);
        lib.ClearResumePosition(vid);
        ReadResumeUpdatedAt(db, vid).ShouldBeNull();
    }

    [Fact]
    public void SetWatched_true_nulls_resume_updated_at()
    {
        var (db, lib, watch, vid) = Seed();
        using var _d = db;
        lib.SetResumePosition(vid, 42.0);
        watch.SetWatched(vid, true);
        ReadResumeUpdatedAt(db, vid).ShouldBeNull();
    }

    private static string? ReadResumeUpdatedAt(TempDb db, long videoId)
    {
        using var conn = db.Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT resume_updated_at FROM videos WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", videoId);
        var v = cmd.ExecuteScalar();
        return v is null or System.DBNull ? null : (string)v;
    }
}
```

> Adjust `TempDb`/`db.Db`/repo constructors to match the existing Core tests.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test VideoShelf.slnx -c Release --nologo -v q --filter "FullyQualifiedName~ResumeUpdatedAtTests"`
Expected: FAIL — `no such column: resume_updated_at`.

- [ ] **Step 3: Add the guarded migration**

In `VideoShelfDb.Migrate()`, directly after the existing guarded `added_at` ALTER block, add an identical-style guard for the new column. Mirror the file's exact existing helper (whether it's a `ColumnExists(...)` check or a `try { ALTER } catch { }`). Example using a column-exists guard:

```csharp
// resume_updated_at: ISO8601 timestamp of the last resume write (Milestone 4 discovery ordering)
if (!ColumnExists(conn, "videos", "resume_updated_at"))
{
    using var alter = conn.CreateCommand();
    alter.CommandText = "ALTER TABLE videos ADD COLUMN resume_updated_at TEXT;";
    alter.ExecuteNonQuery();
}
```

If the existing pattern is `try/catch`, use that exact shape instead. Do not invent a `ColumnExists` helper if one doesn't exist — reuse what's there.

- [ ] **Step 4: Maintain the column in the repos**

In `LibraryRepository.SetResumePosition`, change the UPDATE to also write the timestamp (keep the existing method signature `void SetResumePosition(long videoId, double seconds)`):

```csharp
cmd.CommandText = "UPDATE videos SET resume_position = @p, resume_updated_at = @t WHERE id = @id;";
cmd.Parameters.AddWithValue("@p", seconds);
cmd.Parameters.AddWithValue("@t", DateTimeOffset.UtcNow.ToString("O"));
cmd.Parameters.AddWithValue("@id", videoId);
```

In `LibraryRepository.ClearResumePosition`, also null the timestamp:

```csharp
cmd.CommandText = "UPDATE videos SET resume_position = NULL, resume_updated_at = NULL WHERE id = @id;";
```

In `WatchRepository.SetWatched`, find the branch where `watched == true` clears resume (`resume_position = NULL`) and add `resume_updated_at = NULL` to that same `SET` clause:

```csharp
// e.g. existing: "UPDATE videos SET watched = 1, resume_position = NULL WHERE id = @id;"
//        becomes: "UPDATE videos SET watched = 1, resume_position = NULL, resume_updated_at = NULL WHERE id = @id;"
```

> Keep the rest of each method byte-for-byte. If the SQL doesn't look like the comments above, STOP and report what it actually is before editing.

- [ ] **Step 5: Run to verify it passes**

Run: `dotnet test VideoShelf.slnx -c Release --nologo -v q --filter "FullyQualifiedName~ResumeUpdatedAtTests"`
Expected: PASS (3 tests). Then run the existing schema/resume tests to confirm no regression:
Run: `dotnet test VideoShelf.slnx -c Release --nologo -v q --filter "FullyQualifiedName~ResumePositionTests|FullyQualifiedName~SchemaMigrationTests|FullyQualifiedName~WatchRepositoryTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```
git add src/VideoShelf.Core/Storage/VideoShelfDb.cs src/VideoShelf.Core/Storage/LibraryRepository.cs src/VideoShelf.Core/Storage/WatchRepository.cs tests/VideoShelf.Core.Tests/Storage/ResumeUpdatedAtTests.cs
git commit -m "feat(core): track resume_updated_at for continue-watching ordering

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 3: DiscoveryScoring + models (Core, pure)

**Files:**
- Create: `src/VideoShelf.Core/Discovery/DiscoveryModels.cs`
- Create: `src/VideoShelf.Core/Discovery/DiscoveryScoring.cs`
- Test: `tests/VideoShelf.Core.Tests/Discovery/DiscoveryScoringTests.cs`

Pure, deterministic scoring (no DB, no clock-of-its-own — `now` is passed in) so the weighting is unit-testable in isolation.

- [ ] **Step 1: Write the failing test**

Create `tests/VideoShelf.Core.Tests/Discovery/DiscoveryScoringTests.cs`:

```csharp
using Shouldly;
using VideoShelf.Core.Discovery;
using Xunit;

namespace VideoShelf.Core.Tests.Discovery;

public sealed class DiscoveryScoringTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RecencyWeight_is_1_at_zero_age_and_half_at_one_halflife()
    {
        DiscoveryScoring.RecencyWeight(Now, Now, halfLifeDays: 14).ShouldBe(1.0, 1e-9);
        DiscoveryScoring.RecencyWeight(Now.AddDays(-14), Now, 14).ShouldBe(0.5, 1e-9);
        DiscoveryScoring.RecencyWeight(Now.AddDays(-28), Now, 14).ShouldBe(0.25, 1e-9);
    }

    [Fact]
    public void RecencyWeight_clamps_future_events_to_1()
    {
        DiscoveryScoring.RecencyWeight(Now.AddDays(5), Now, 14).ShouldBe(1.0, 1e-9);
    }

    [Fact]
    public void BuildTagAffinity_accumulates_recency_weighted_per_tag()
    {
        var events = new[]
        {
            new WatchedTag("comedy", Now),               // weight 1.0
            new WatchedTag("comedy", Now.AddDays(-14)),  // weight 0.5
            new WatchedTag("drama", Now.AddDays(-14)),   // weight 0.5
        };
        var aff = DiscoveryScoring.BuildTagAffinity(events, Now, halfLifeDays: 14);
        aff["comedy"].ShouldBe(1.5, 1e-9);
        aff["drama"].ShouldBe(0.5, 1e-9);
    }

    [Fact]
    public void ScoreSection_zero_when_no_tag_overlap()
    {
        var aff = new Dictionary<string, double> { ["comedy"] = 2.0 };
        DiscoveryScoring.ScoreSection(new[] { "horror" }, aff, unwatchedCount: 5, episodeCount: 5)
            .ShouldBe(0.0, 1e-9);
    }

    [Fact]
    public void ScoreSection_weights_overlap_by_unwatched_ratio()
    {
        var aff = new Dictionary<string, double> { ["comedy"] = 2.0 };
        // overlap = 2.0; fully unwatched -> *(0.5 + 0.5*1.0) = *1.0 => 2.0
        DiscoveryScoring.ScoreSection(new[] { "comedy" }, aff, 10, 10).ShouldBe(2.0, 1e-9);
        // fully watched -> *(0.5 + 0.5*0) = *0.5 => 1.0
        DiscoveryScoring.ScoreSection(new[] { "comedy" }, aff, 0, 10).ShouldBe(1.0, 1e-9);
    }

    [Fact]
    public void ScoreSection_sums_multiple_overlapping_tags()
    {
        var aff = new Dictionary<string, double> { ["comedy"] = 2.0, ["drama"] = 1.0 };
        // overlap = 3.0; half unwatched -> *(0.5 + 0.5*0.5)=*0.75 => 2.25
        DiscoveryScoring.ScoreSection(new[] { "comedy", "drama" }, aff, 5, 10).ShouldBe(2.25, 1e-9);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test VideoShelf.slnx -c Release --nologo -v q --filter "FullyQualifiedName~DiscoveryScoringTests"`
Expected: FAIL — types not defined.

- [ ] **Step 3: Write the models + scoring**

Create `src/VideoShelf.Core/Discovery/DiscoveryModels.cs`:

```csharp
namespace VideoShelf.Core.Discovery;

/// <summary>A resumable video for the Continue-watching rail.</summary>
public sealed record ContinueWatchingItem(
    long VideoId, long SeriesId, long SectionId, string SeriesTitle, bool IsStandalone,
    int EpisodeNo, double ResumePosition, double? Duration, string? ThumbnailSeedPath);

/// <summary>A video for the Recently-added / Recently-watched rails.</summary>
public sealed record RecencyItem(
    long VideoId, long SeriesId, long SectionId, string SeriesTitle, bool IsStandalone,
    int EpisodeNo, bool Watched, string? ThumbnailSeedPath);

/// <summary>A scored section for For-you / Pick-a-tag / More-from-section rails.</summary>
public sealed record SectionSuggestion(
    long SectionId, string DisplayName, int SeriesCount, int EpisodeCount, int UnwatchedCount,
    IReadOnlyList<string> Tags, double Score);

/// <summary>One (tag, time) pair derived from the watch history, for affinity scoring.</summary>
public sealed record WatchedTag(string Tag, DateTimeOffset WatchedAt);
```

Create `src/VideoShelf.Core/Discovery/DiscoveryScoring.cs`:

```csharp
namespace VideoShelf.Core.Discovery;

public static class DiscoveryScoring
{
    /// <summary>Exponential recency decay: 1.0 at age 0, halves every <paramref name="halfLifeDays"/>. Future events clamp to 1.0.</summary>
    public static double RecencyWeight(DateTimeOffset eventTime, DateTimeOffset now, double halfLifeDays)
    {
        var ageDays = (now - eventTime).TotalDays;
        if (ageDays <= 0) return 1.0;
        return Math.Pow(0.5, ageDays / halfLifeDays);
    }

    /// <summary>Sum of recency weights per tag across the watch history.</summary>
    public static IReadOnlyDictionary<string, double> BuildTagAffinity(
        IEnumerable<WatchedTag> events, DateTimeOffset now, double halfLifeDays)
    {
        var affinity = new Dictionary<string, double>();
        foreach (var e in events)
        {
            var w = RecencyWeight(e.WatchedAt, now, halfLifeDays);
            affinity[e.Tag] = affinity.TryGetValue(e.Tag, out var cur) ? cur + w : w;
        }
        return affinity;
    }

    /// <summary>
    /// Section relevance: summed affinity of overlapping tags, modulated toward mostly-unwatched content.
    /// Zero when there is no tag overlap.
    /// </summary>
    public static double ScoreSection(
        IReadOnlyList<string> sectionTags, IReadOnlyDictionary<string, double> affinity,
        int unwatchedCount, int episodeCount)
    {
        double overlap = 0;
        foreach (var t in sectionTags)
            if (affinity.TryGetValue(t, out var a)) overlap += a;
        if (overlap <= 0) return 0;
        var unwatchedRatio = episodeCount <= 0 ? 0 : (double)unwatchedCount / episodeCount;
        return overlap * (0.5 + 0.5 * unwatchedRatio);
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test VideoShelf.slnx -c Release --nologo -v q --filter "FullyQualifiedName~DiscoveryScoringTests"`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```
git add src/VideoShelf.Core/Discovery/DiscoveryModels.cs src/VideoShelf.Core/Discovery/DiscoveryScoring.cs tests/VideoShelf.Core.Tests/Discovery/DiscoveryScoringTests.cs
git commit -m "feat(core): add discovery models and weighted scoring helpers

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 4: DiscoveryRepository (Core queries)

**Files:**
- Create: `src/VideoShelf.Core/Discovery/DiscoveryRepository.cs`
- Test: `tests/VideoShelf.Core.Tests/Discovery/DiscoveryRepositoryTests.cs`

Ties the SQL to the scoring. Constructor depends on `VideoShelfDb`, `LibraryRepository` (reuse `GetSeriesSummaries` for More-from-section), and `TagRepository`. `GetForYou` takes `now` so recency is testable.

- [ ] **Step 1: Write the failing test**

Create `tests/VideoShelf.Core.Tests/Discovery/DiscoveryRepositoryTests.cs`. This seeds videos, sets resume positions and `resume_updated_at`/`watch_events` at controlled times via direct SQL for determinism:

```csharp
using Microsoft.Data.Sqlite;
using Shouldly;
using VideoShelf.Core.Discovery;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;
using Xunit;

namespace VideoShelf.Core.Tests.Discovery;

public sealed class DiscoveryRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 11, 12, 0, 0, TimeSpan.Zero);

    private sealed record Fixture(TempDb Db, LibraryRepository Lib, WatchRepository Watch,
        TagRepository Tags, DiscoveryRepository Disc);

    private static Fixture NewFixture()
    {
        var db = new TempDb();
        var lib = new LibraryRepository(db.Db);
        var watch = new WatchRepository(db.Db);
        var tags = new TagRepository(db.Db);
        var disc = new DiscoveryRepository(db.Db, lib, tags);
        return new Fixture(db, lib, watch, tags, disc);
    }

    private static long AddVideo(Fixture f, long sectionId, string series, bool standalone, int ep)
    {
        var ser = f.Lib.UpsertSeries(sectionId, series, standalone);
        return f.Lib.UpsertVideo(ser, $@"C:\m\{series}\e{ep:00}.mkv", ep, "mkv");
    }

    private static void SetRaw(TempDb db, string sql, params (string, object)[] ps)
    {
        using var conn = db.Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in ps) cmd.Parameters.AddWithValue(k, v);
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public void ContinueWatching_returns_resumable_newest_first_and_excludes_missing()
    {
        var f = NewFixture(); using var _d = f.Db;
        var src = f.Lib.UpsertSource(@"C:\m", "M");
        var sec = f.Lib.UpsertSection(src, "S");
        var a = AddVideo(f, sec, "Alpha", false, 1);
        var b = AddVideo(f, sec, "Beta", false, 1);
        f.Lib.SetResumePosition(a, 10);
        f.Lib.SetResumePosition(b, 20);
        // force deterministic order: b resumed more recently than a
        SetRaw(f.Db, "UPDATE videos SET resume_updated_at=@t WHERE id=@id",
            ("@t", Now.AddMinutes(-10).ToString("O")), ("@id", a));
        SetRaw(f.Db, "UPDATE videos SET resume_updated_at=@t WHERE id=@id",
            ("@t", Now.ToString("O")), ("@id", b));

        var items = f.Disc.GetContinueWatching(limit: 10);
        items.Select(i => i.VideoId).ShouldBe(new[] { b, a });
        items[0].ResumePosition.ShouldBe(20);
    }

    [Fact]
    public void RecentlyAdded_orders_by_added_at_desc()
    {
        var f = NewFixture(); using var _d = f.Db;
        var src = f.Lib.UpsertSource(@"C:\m", "M");
        var sec = f.Lib.UpsertSection(src, "S");
        var a = AddVideo(f, sec, "Alpha", false, 1);
        var b = AddVideo(f, sec, "Beta", false, 1);
        SetRaw(f.Db, "UPDATE videos SET added_at=@t WHERE id=@id", ("@t", "2026-06-01T00:00:00.000Z"), ("@id", a));
        SetRaw(f.Db, "UPDATE videos SET added_at=@t WHERE id=@id", ("@t", "2026-06-10T00:00:00.000Z"), ("@id", b));
        f.Disc.GetRecentlyAdded(10).Select(i => i.VideoId).ShouldBe(new[] { b, a });
    }

    [Fact]
    public void RecentlyWatched_orders_by_latest_watch_event()
    {
        var f = NewFixture(); using var _d = f.Db;
        var src = f.Lib.UpsertSource(@"C:\m", "M");
        var sec = f.Lib.UpsertSection(src, "S");
        var a = AddVideo(f, sec, "Alpha", false, 1);
        var b = AddVideo(f, sec, "Beta", false, 1);
        SetRaw(f.Db, "INSERT INTO watch_events (video_id, watched_at) VALUES (@v,@t)",
            ("@v", a), ("@t", "2026-06-05T00:00:00.000Z"));
        SetRaw(f.Db, "INSERT INTO watch_events (video_id, watched_at) VALUES (@v,@t)",
            ("@v", b), ("@t", "2026-06-09T00:00:00.000Z"));
        f.Disc.GetRecentlyWatched(10).Select(i => i.VideoId).ShouldBe(new[] { b, a });
    }

    [Fact]
    public void ForYou_suggests_unwatched_sections_sharing_tags_with_history()
    {
        var f = NewFixture(); using var _d = f.Db;
        var src = f.Lib.UpsertSource(@"C:\m", "M");
        var watchedSec = f.Lib.UpsertSection(src, "Watched");
        var candidate = f.Lib.UpsertSection(src, "Candidate");
        var unrelated = f.Lib.UpsertSection(src, "Unrelated");
        f.Tags.AddTag(watchedSec, "comedy");
        f.Tags.AddTag(candidate, "comedy");      // shares tag -> suggested
        f.Tags.AddTag(unrelated, "horror");      // no overlap -> excluded
        var w = AddVideo(f, watchedSec, "WShow", false, 1);
        AddVideo(f, candidate, "CShow", false, 1);
        AddVideo(f, unrelated, "UShow", false, 1);
        SetRaw(f.Db, "INSERT INTO watch_events (video_id, watched_at) VALUES (@v,@t)",
            ("@v", w), ("@t", Now.AddDays(-1).ToString("O")));

        var sugg = f.Disc.GetForYou(limit: 10, now: Now);
        sugg.Select(s => s.SectionId).ShouldBe(new[] { candidate });
        sugg[0].Score.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void GetSectionsByTags_returns_matching_sections_scored()
    {
        var f = NewFixture(); using var _d = f.Db;
        var src = f.Lib.UpsertSource(@"C:\m", "M");
        var s1 = f.Lib.UpsertSection(src, "One");
        var s2 = f.Lib.UpsertSection(src, "Two");
        f.Tags.AddTag(s1, "comedy");
        f.Tags.AddTag(s2, "drama");
        AddVideo(f, s1, "OneShow", false, 1);
        AddVideo(f, s2, "TwoShow", false, 1);
        var hits = f.Disc.GetSectionsByTags(new[] { "comedy" }, limit: 10);
        hits.Select(h => h.SectionId).ShouldBe(new[] { s1 });
    }

    [Fact]
    public void GetMoreFromSection_excludes_the_current_series()
    {
        var f = NewFixture(); using var _d = f.Db;
        var src = f.Lib.UpsertSource(@"C:\m", "M");
        var sec = f.Lib.UpsertSection(src, "S");
        var serA = f.Lib.UpsertSeries(sec, "Alpha", false);
        var serB = f.Lib.UpsertSeries(sec, "Beta", false);
        f.Lib.UpsertVideo(serA, @"C:\m\Alpha\e01.mkv", 1, "mkv");
        f.Lib.UpsertVideo(serB, @"C:\m\Beta\e01.mkv", 1, "mkv");
        var more = f.Disc.GetMoreFromSection(sec, excludeSeriesId: serA, limit: 10);
        more.Select(s => s.SeriesId).ShouldNotContain(serA);
        more.Select(s => s.SeriesId).ShouldContain(serB);
    }
}
```

> If `watch_events`/`videos` column names differ from what the raw SQL assumes (`watched_at`, `added_at`, `resume_updated_at`), correct them to match the schema you confirmed in Task 2 — do not change the schema.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test VideoShelf.slnx -c Release --nologo -v q --filter "FullyQualifiedName~DiscoveryRepositoryTests"`
Expected: FAIL — `DiscoveryRepository` not defined.

- [ ] **Step 3: Write the repository**

Create `src/VideoShelf.Core/Discovery/DiscoveryRepository.cs`. Add a `using` for the namespace that declares `SeriesSummary`/`LibraryRepository`/`TagRepository` (the Storage namespace):

```csharp
using Microsoft.Data.Sqlite;
using VideoShelf.Core.Storage; // namespace of LibraryRepository, TagRepository, SeriesSummary

namespace VideoShelf.Core.Discovery;

public sealed class DiscoveryRepository(VideoShelfDb db, LibraryRepository library, TagRepository tags)
{
    private const double HalfLifeDays = 14.0;
    private const int HistoryWindow = 500;

    public IReadOnlyList<ContinueWatchingItem> GetContinueWatching(int limit)
    {
        const string sql = """
            SELECT v.id, v.series_id, s.section_id, s.base_title, s.is_standalone,
                   v.episode_no, v.resume_position, v.duration, v.thumbnail_path
            FROM videos v
            JOIN series s ON s.id = v.series_id
            WHERE v.resume_position IS NOT NULL AND v.missing = 0
            ORDER BY v.resume_updated_at DESC, v.id DESC
            LIMIT @limit;
            """;
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@limit", limit);
        var result = new List<ContinueWatchingItem>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            result.Add(new ContinueWatchingItem(
                VideoId: r.GetInt64(0), SeriesId: r.GetInt64(1), SectionId: r.GetInt64(2),
                SeriesTitle: r.GetString(3), IsStandalone: r.GetInt64(4) != 0,
                EpisodeNo: r.GetInt32(5),
                ResumePosition: r.GetDouble(6),
                Duration: r.IsDBNull(7) ? null : r.GetDouble(7),
                ThumbnailSeedPath: r.IsDBNull(8) ? null : r.GetString(8)));
        }
        return result;
    }

    public IReadOnlyList<RecencyItem> GetRecentlyAdded(int limit) =>
        ReadRecency("""
            SELECT v.id, v.series_id, s.section_id, s.base_title, s.is_standalone,
                   v.episode_no, v.watched, v.thumbnail_path
            FROM videos v
            JOIN series s ON s.id = v.series_id
            WHERE v.missing = 0
            ORDER BY v.added_at DESC, v.id DESC
            LIMIT @limit;
            """, limit);

    public IReadOnlyList<RecencyItem> GetRecentlyWatched(int limit) =>
        ReadRecency("""
            SELECT v.id, v.series_id, s.section_id, s.base_title, s.is_standalone,
                   v.episode_no, v.watched, v.thumbnail_path, MAX(we.watched_at) AS last_watched
            FROM watch_events we
            JOIN videos v ON v.id = we.video_id
            JOIN series s ON s.id = v.series_id
            WHERE v.missing = 0
            GROUP BY v.id
            ORDER BY last_watched DESC
            LIMIT @limit;
            """, limit);

    private IReadOnlyList<RecencyItem> ReadRecency(string sql, int limit)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@limit", limit);
        var result = new List<RecencyItem>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            result.Add(new RecencyItem(
                VideoId: r.GetInt64(0), SeriesId: r.GetInt64(1), SectionId: r.GetInt64(2),
                SeriesTitle: r.GetString(3), IsStandalone: r.GetInt64(4) != 0,
                EpisodeNo: r.GetInt32(5), Watched: r.GetInt64(6) != 0,
                ThumbnailSeedPath: r.IsDBNull(7) ? null : r.GetString(7)));
        }
        return result;
    }

    public IReadOnlyList<SectionSuggestion> GetForYou(int limit, DateTimeOffset now)
    {
        var history = ReadWatchedTags();
        if (history.Count == 0) return [];
        var affinity = DiscoveryScoring.BuildTagAffinity(history, now, HalfLifeDays);
        var watchedSections = ReadWatchedSectionIds();

        var scored = new List<SectionSuggestion>();
        foreach (var sec in ReadSectionStats())
        {
            if (watchedSections.Contains(sec.SectionId)) continue; // suggest *new* sections
            var secTags = tags.GetTags(sec.SectionId);
            var score = DiscoveryScoring.ScoreSection(secTags, affinity, sec.UnwatchedCount, sec.EpisodeCount);
            if (score <= 0) continue;
            scored.Add(sec with { Tags = secTags, Score = score });
        }
        return scored
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(limit).ToList();
    }

    public IReadOnlyList<SectionSuggestion> GetSectionsByTags(IReadOnlyList<string> selectedTags, int limit)
    {
        var norm = selectedTags.Select(TagRepository.Normalize).Where(t => t.Length > 0).Distinct().ToList();
        if (norm.Count == 0) return [];
        var flatAffinity = norm.ToDictionary(t => t, _ => 1.0);

        var scored = new List<SectionSuggestion>();
        foreach (var sec in ReadSectionStats())
        {
            var secTags = tags.GetTags(sec.SectionId);
            var score = DiscoveryScoring.ScoreSection(secTags, flatAffinity, sec.UnwatchedCount, sec.EpisodeCount);
            if (score <= 0) continue;
            scored.Add(sec with { Tags = secTags, Score = score });
        }
        return scored
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(limit).ToList();
    }

    public IReadOnlyList<SeriesSummary> GetMoreFromSection(long sectionId, long excludeSeriesId, int limit) =>
        library.GetSeriesSummaries(sectionId)
            .Where(s => s.SeriesId != excludeSeriesId)
            .Take(limit).ToList();

    // --- helpers ---

    private List<WatchedTag> ReadWatchedTags()
    {
        const string sql = """
            SELECT st.tag, we.watched_at
            FROM watch_events we
            JOIN videos v ON v.id = we.video_id
            JOIN series s ON s.id = v.series_id
            JOIN section_tags st ON st.section_id = s.section_id
            ORDER BY we.watched_at DESC
            LIMIT @window;
            """;
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@window", HistoryWindow);
        var list = new List<WatchedTag>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new WatchedTag(r.GetString(0), DateTimeOffset.Parse(r.GetString(1),
                null, System.Globalization.DateTimeStyles.RoundtripKind)));
        return list;
    }

    private HashSet<long> ReadWatchedSectionIds()
    {
        const string sql = """
            SELECT DISTINCT s.section_id
            FROM watch_events we
            JOIN videos v ON v.id = we.video_id
            JOIN series s ON s.id = v.series_id;
            """;
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var set = new HashSet<long>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) set.Add(r.GetInt64(0));
        return set;
    }

    private List<SectionSuggestion> ReadSectionStats()
    {
        const string sql = """
            SELECT sec.id, sec.display_name,
                   (SELECT COUNT(*) FROM series s2 WHERE s2.section_id = sec.id) AS series_count,
                   (SELECT COUNT(*) FROM videos v2 JOIN series s3 ON s3.id = v2.series_id
                      WHERE s3.section_id = sec.id AND v2.missing = 0) AS episode_count,
                   (SELECT COUNT(*) FROM videos v3 JOIN series s4 ON s4.id = v3.series_id
                      WHERE s4.section_id = sec.id AND v3.missing = 0 AND v3.watched = 0) AS unwatched_count
            FROM sections sec
            ORDER BY sec.display_name;
            """;
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var list = new List<SectionSuggestion>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new SectionSuggestion(
                SectionId: r.GetInt64(0), DisplayName: r.GetString(1),
                SeriesCount: r.GetInt32(2), EpisodeCount: r.GetInt32(3), UnwatchedCount: r.GetInt32(4),
                Tags: [], Score: 0));
        return list;
    }
}
```

> If `sections` uses `display_name` differently, or `series.is_standalone`/`videos.watched` are named otherwise, fix the column names to match the real schema. The shapes and ordering must stay.

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test VideoShelf.slnx -c Release --nologo -v q --filter "FullyQualifiedName~DiscoveryRepositoryTests"`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```
git add src/VideoShelf.Core/Discovery/DiscoveryRepository.cs tests/VideoShelf.Core.Tests/Discovery/DiscoveryRepositoryTests.cs
git commit -m "feat(core): add DiscoveryRepository rail queries

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 5: Discovery card/chip VMs + DiscoveryViewModel (App)

**Files:**
- Create: `src/VideoShelf.App/ViewModels/Discovery/ContinueWatchingCardViewModel.cs`
- Create: `src/VideoShelf.App/ViewModels/Discovery/RecencyCardViewModel.cs`
- Create: `src/VideoShelf.App/ViewModels/Discovery/SectionCardViewModel.cs`
- Create: `src/VideoShelf.App/ViewModels/Discovery/TagChipViewModel.cs`
- Create: `src/VideoShelf.App/ViewModels/Discovery/DiscoveryViewModel.cs`
- Test: `tests/VideoShelf.App.Tests/DiscoveryViewModelTests.cs`

`DiscoveryViewModel` loads the rails and exposes `PlayRequested` (raised with an `EpisodeView` resolved from `LibraryRepository.GetEpisodes` at click time, so playback reuses the canonical title/missing logic) and `SectionOpenRequested` (a `long sectionId`). Thumbnails load lazily — construction does no thumbnail work, so VM tests need no real thumbnailer.

- [ ] **Step 1: Read first, then write the failing test**

Read `ViewModels/SeriesViewModel.cs` to copy its **exact** `LoadThumbnailAsync(IThumbnailService ...)` pattern (method name, the snapshotter call, the property it sets). The card VMs mirror it. Read an existing App test (e.g. `LibraryViewModelTests.cs`) for the `AppTempDb` seeding helper and namespaces.

Create `tests/VideoShelf.App.Tests/DiscoveryViewModelTests.cs`:

```csharp
using Shouldly;
using VideoShelf.App.ViewModels.Discovery;
using VideoShelf.Core.Discovery;
using VideoShelf.Core.Storage;
using VideoShelf.App.Tests.TestSupport;
using Xunit;

namespace VideoShelf.App.Tests;

public sealed class DiscoveryViewModelTests
{
    private sealed record Fx(AppTempDb Db, LibraryRepository Lib, WatchRepository Watch,
        TagRepository Tags, DiscoveryRepository Disc, DiscoveryViewModel Vm);

    private static Fx NewFx()
    {
        var db = new AppTempDb();
        var lib = new LibraryRepository(db.Db);
        var watch = new WatchRepository(db.Db);
        var tags = new TagRepository(db.Db);
        var disc = new DiscoveryRepository(db.Db, lib, tags);
        var vm = new DiscoveryViewModel(disc, lib, tags);
        return new Fx(db, lib, watch, tags, disc, vm);
    }

    [Fact]
    public async Task LoadAsync_populates_continue_watching_rail()
    {
        var f = NewFx(); using var _d = f.Db;
        var src = f.Lib.UpsertSource(@"C:\m", "M");
        var sec = f.Lib.UpsertSection(src, "S");
        var ser = f.Lib.UpsertSeries(sec, "Show", false);
        var vid = f.Lib.UpsertVideo(ser, @"C:\m\Show\e01.mkv", 1, "mkv");
        f.Lib.SetResumePosition(vid, 30);

        await f.Vm.LoadAsync();

        f.Vm.ContinueWatching.Count.ShouldBe(1);
        f.Vm.HasContinueWatching.ShouldBeTrue();
        f.Vm.ContinueWatching[0].VideoId.ShouldBe(vid);
    }

    [Fact]
    public async Task Continue_card_Play_raises_PlayRequested_with_matching_episode()
    {
        var f = NewFx(); using var _d = f.Db;
        var src = f.Lib.UpsertSource(@"C:\m", "M");
        var sec = f.Lib.UpsertSection(src, "S");
        var ser = f.Lib.UpsertSeries(sec, "Show", false);
        var vid = f.Lib.UpsertVideo(ser, @"C:\m\Show\e01.mkv", 1, "mkv");
        f.Lib.SetResumePosition(vid, 30);
        await f.Vm.LoadAsync();

        EpisodeView? played = null;
        f.Vm.PlayRequested += (_, e) => played = e;
        f.Vm.ContinueWatching[0].PlayCommand.Execute(null);

        played.ShouldNotBeNull();
        played!.VideoId.ShouldBe(vid);
    }

    [Fact]
    public async Task Section_card_Open_raises_SectionOpenRequested()
    {
        var f = NewFx(); using var _d = f.Db;
        var src = f.Lib.UpsertSource(@"C:\m", "M");
        var watchedSec = f.Lib.UpsertSection(src, "Watched");
        var candidate = f.Lib.UpsertSection(src, "Candidate");
        f.Tags.AddTag(watchedSec, "comedy");
        f.Tags.AddTag(candidate, "comedy");
        var ser = f.Lib.UpsertSeries(watchedSec, "WShow", false);
        var wv = f.Lib.UpsertVideo(ser, @"C:\m\WShow\e01.mkv", 1, "mkv");
        f.Lib.UpsertVideo(f.Lib.UpsertSeries(candidate, "CShow", false), @"C:\m\CShow\e01.mkv", 1, "mkv");
        f.Watch.SetWatched(wv, true);

        await f.Vm.LoadAsync();
        f.Vm.ForYou.ShouldNotBeEmpty();

        long? opened = null;
        f.Vm.SectionOpenRequested += (_, id) => opened = id;
        f.Vm.ForYou[0].OpenCommand.Execute(null);
        opened.ShouldBe(candidate);
    }

    [Fact]
    public async Task ToggleTag_recomputes_tag_results()
    {
        var f = NewFx(); using var _d = f.Db;
        var src = f.Lib.UpsertSource(@"C:\m", "M");
        var s1 = f.Lib.UpsertSection(src, "One");
        f.Tags.AddTag(s1, "comedy");
        f.Lib.UpsertVideo(f.Lib.UpsertSeries(s1, "OneShow", false), @"C:\m\OneShow\e01.mkv", 1, "mkv");

        await f.Vm.LoadAsync();
        var chip = f.Vm.AvailableTags.First(t => t.Tag == "comedy");
        f.Vm.ToggleTagCommand.Execute(chip);

        chip.IsSelected.ShouldBeTrue();
        f.Vm.TagResults.Select(r => r.SectionId).ShouldContain(s1);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test VideoShelf.slnx -c Release --nologo -v q --filter "FullyQualifiedName~DiscoveryViewModelTests"`
Expected: FAIL — VM types not defined.

- [ ] **Step 3: Write the card/chip VMs**

`ContinueWatchingCardViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoShelf.Core.Discovery;

namespace VideoShelf.App.ViewModels.Discovery;

public sealed partial class ContinueWatchingCardViewModel(ContinueWatchingItem item) : ObservableObject
{
    public long VideoId => item.VideoId;
    public long SeriesId => item.SeriesId;
    public string SeriesTitle => item.SeriesTitle;
    public string EpisodeLabel => item.IsStandalone ? item.SeriesTitle : $"Episode {item.EpisodeNo}";
    public string? ThumbnailSeedPath => item.ThumbnailSeedPath;
    public double ProgressFraction =>
        item.Duration is > 0 ? Math.Clamp(item.ResumePosition / item.Duration.Value, 0, 1) : 0;

    [ObservableProperty] private string? thumbnailPath;

    public event EventHandler? PlayInvoked;
    [RelayCommand] private void Play() => PlayInvoked?.Invoke(this, EventArgs.Empty);
}
```

`RecencyCardViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoShelf.Core.Discovery;

namespace VideoShelf.App.ViewModels.Discovery;

public sealed partial class RecencyCardViewModel(RecencyItem item) : ObservableObject
{
    public long VideoId => item.VideoId;
    public long SeriesId => item.SeriesId;
    public string SeriesTitle => item.SeriesTitle;
    public string EpisodeLabel => item.IsStandalone ? item.SeriesTitle : $"Episode {item.EpisodeNo}";
    public bool Watched => item.Watched;
    public string? ThumbnailSeedPath => item.ThumbnailSeedPath;

    [ObservableProperty] private string? thumbnailPath;

    public event EventHandler? PlayInvoked;
    [RelayCommand] private void Play() => PlayInvoked?.Invoke(this, EventArgs.Empty);
}
```

`SectionCardViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoShelf.Core.Discovery;

namespace VideoShelf.App.ViewModels.Discovery;

public sealed partial class SectionCardViewModel(SectionSuggestion item) : ObservableObject
{
    public long SectionId => item.SectionId;
    public string DisplayName => item.DisplayName;
    public int SeriesCount => item.SeriesCount;
    public int UnwatchedCount => item.UnwatchedCount;
    public bool HasUnwatched => item.UnwatchedCount > 0;
    public string TagsLabel => string.Join(" · ", item.Tags);

    public event EventHandler? OpenInvoked;
    [RelayCommand] private void Open() => OpenInvoked?.Invoke(this, EventArgs.Empty);
}
```

`TagChipViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;

namespace VideoShelf.App.ViewModels.Discovery;

public sealed partial class TagChipViewModel(string tag, int sectionCount) : ObservableObject
{
    public string Tag => tag;
    public int SectionCount => sectionCount;
    public string Label => $"{tag} ({sectionCount})";
    [ObservableProperty] private bool isSelected;
}
```

- [ ] **Step 4: Write DiscoveryViewModel**

`DiscoveryViewModel.cs`. **No `ConfigureAwait(false)`** — collections are UI-bound. DB reads run inside `Task.Run`; the continuation populates collections on the captured context.

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoShelf.Core.Discovery;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.ViewModels.Discovery;

public sealed partial class DiscoveryViewModel(
    DiscoveryRepository discovery, LibraryRepository library, TagRepository tags) : ObservableObject
{
    private const int RailLimit = 24;

    public ObservableCollection<ContinueWatchingCardViewModel> ContinueWatching { get; } = [];
    public ObservableCollection<RecencyCardViewModel> RecentlyAdded { get; } = [];
    public ObservableCollection<RecencyCardViewModel> RecentlyWatched { get; } = [];
    public ObservableCollection<SectionCardViewModel> ForYou { get; } = [];
    public ObservableCollection<TagChipViewModel> AvailableTags { get; } = [];
    public ObservableCollection<SectionCardViewModel> TagResults { get; } = [];

    public bool HasContinueWatching => ContinueWatching.Count > 0;
    public bool HasRecentlyAdded => RecentlyAdded.Count > 0;
    public bool HasRecentlyWatched => RecentlyWatched.Count > 0;
    public bool HasForYou => ForYou.Count > 0;
    public bool HasTags => AvailableTags.Count > 0;
    public bool HasTagResults => TagResults.Count > 0;
    public bool IsEmpty =>
        !HasContinueWatching && !HasRecentlyAdded && !HasRecentlyWatched && !HasForYou && !HasTags;

    public event EventHandler<EpisodeView>? PlayRequested;
    public event EventHandler<long>? SectionOpenRequested;

    public async Task LoadAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var data = await Task.Run(() => (
            cont: discovery.GetContinueWatching(RailLimit),
            added: discovery.GetRecentlyAdded(RailLimit),
            watched: discovery.GetRecentlyWatched(RailLimit),
            forYou: discovery.GetForYou(RailLimit, now),
            tagCounts: tags.GetTagCounts()));

        Fill(ContinueWatching, data.cont, MakeContinueCard);
        Fill(RecentlyAdded, data.added, MakeRecencyCard);
        Fill(RecentlyWatched, data.watched, MakeRecencyCard);
        Fill(ForYou, data.forYou, MakeSectionCard);

        AvailableTags.Clear();
        foreach (var tc in data.tagCounts) AvailableTags.Add(new TagChipViewModel(tc.Tag, tc.SectionCount));
        TagResults.Clear();

        RaiseAllHasFlags();
    }

    [RelayCommand]
    private async Task ToggleTag(TagChipViewModel chip)
    {
        chip.IsSelected = !chip.IsSelected;
        var selected = AvailableTags.Where(t => t.IsSelected).Select(t => t.Tag).ToList();
        var results = selected.Count == 0
            ? []
            : await Task.Run(() => discovery.GetSectionsByTags(selected, RailLimit));
        Fill(TagResults, results, MakeSectionCard);
        OnPropertyChanged(nameof(HasTagResults));
    }

    private ContinueWatchingCardViewModel MakeContinueCard(ContinueWatchingItem i)
    {
        var card = new ContinueWatchingCardViewModel(i);
        card.PlayInvoked += (_, _) => RaisePlay(i.SeriesId, i.VideoId);
        return card;
    }

    private RecencyCardViewModel MakeRecencyCard(RecencyItem i)
    {
        var card = new RecencyCardViewModel(i);
        card.PlayInvoked += (_, _) => RaisePlay(i.SeriesId, i.VideoId);
        return card;
    }

    private SectionCardViewModel MakeSectionCard(SectionSuggestion s)
    {
        var card = new SectionCardViewModel(s);
        card.OpenInvoked += (_, _) => SectionOpenRequested?.Invoke(this, s.SectionId);
        return card;
    }

    private void RaisePlay(long seriesId, long videoId)
    {
        var episode = library.GetEpisodes(seriesId).FirstOrDefault(e => e.VideoId == videoId);
        if (episode is not null) PlayRequested?.Invoke(this, episode);
    }

    private static void Fill<TItem, TCard>(
        ObservableCollection<TCard> target, IReadOnlyList<TItem> items, Func<TItem, TCard> make)
    {
        target.Clear();
        foreach (var i in items) target.Add(make(i));
    }

    private void RaiseAllHasFlags()
    {
        OnPropertyChanged(nameof(HasContinueWatching));
        OnPropertyChanged(nameof(HasRecentlyAdded));
        OnPropertyChanged(nameof(HasRecentlyWatched));
        OnPropertyChanged(nameof(HasForYou));
        OnPropertyChanged(nameof(HasTags));
        OnPropertyChanged(nameof(HasTagResults));
        OnPropertyChanged(nameof(IsEmpty));
    }
}
```

> `EpisodeView` lives in the Storage namespace (per the digest). Confirm the `using` resolves; if `GetEpisodes` returns a differently-named property than `VideoId`, adjust the `FirstOrDefault` predicate.

- [ ] **Step 5: Run to verify it passes**

Run: `dotnet test VideoShelf.slnx -c Release --nologo -v q --filter "FullyQualifiedName~DiscoveryViewModelTests"`
Expected: PASS (4 tests).

- [ ] **Step 6: Commit**

```
git add src/VideoShelf.App/ViewModels/Discovery tests/VideoShelf.App.Tests/DiscoveryViewModelTests.cs
git commit -m "feat(app): add discovery rail view models

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 6: SectionDetailViewModel (App, tag editor)

**Files:**
- Create: `src/VideoShelf.App/ViewModels/SectionDetailViewModel.cs`
- Test: `tests/VideoShelf.App.Tests/SectionDetailViewModelTests.cs`

A dedicated section page: loads the section's series (reusing the existing `SeriesViewModel`) plus a tag editor with add/remove and autocomplete suggestions sourced from `TagRepository.GetAllTags()`.

- [ ] **Step 1: Read first, then write the failing test**

Read `ViewModels/SeriesViewModel.cs` for its constructor signature (you'll instantiate it for the series list) and `ViewModels/SectionViewModel.cs` for how it currently loads series. Read `LibraryViewModelTests.cs` for the `AppTempDb` helper.

Create `tests/VideoShelf.App.Tests/SectionDetailViewModelTests.cs`:

```csharp
using Shouldly;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Storage;
using VideoShelf.App.Tests.TestSupport;
using Xunit;

namespace VideoShelf.App.Tests;

public sealed class SectionDetailViewModelTests
{
    private sealed record Fx(AppTempDb Db, LibraryRepository Lib, TagRepository Tags,
        SectionDetailViewModel Vm, long SectionId);

    private static Fx NewFx()
    {
        var db = new AppTempDb();
        var lib = new LibraryRepository(db.Db);
        var tags = new TagRepository(db.Db);
        var src = lib.UpsertSource(@"C:\m", "M");
        var sec = lib.UpsertSection(src, "Creator A");
        lib.UpsertVideo(lib.UpsertSeries(sec, "Show", false), @"C:\m\Show\e01.mkv", 1, "mkv");
        var vm = new SectionDetailViewModel(lib, tags);
        return new Fx(db, lib, tags, vm, sec);
    }

    [Fact]
    public async Task LoadAsync_loads_name_series_and_existing_tags()
    {
        var f = NewFx(); using var _d = f.Db;
        f.Tags.AddTag(f.SectionId, "comedy");
        await f.Vm.LoadAsync(f.SectionId);
        f.Vm.DisplayName.ShouldBe("Creator A");
        f.Vm.SeriesList.ShouldNotBeEmpty();
        f.Vm.Tags.ShouldBe(new[] { "comedy" });
    }

    [Fact]
    public async Task AddTag_persists_and_appears_in_collection()
    {
        var f = NewFx(); using var _d = f.Db;
        await f.Vm.LoadAsync(f.SectionId);
        f.Vm.TagInput = "Drama";
        f.Vm.AddTagCommand.Execute(null);
        f.Vm.Tags.ShouldContain("drama");
        f.Tags.GetTags(f.SectionId).ShouldContain("drama");
        f.Vm.TagInput.ShouldBeEmpty();
    }

    [Fact]
    public async Task RemoveTag_persists_removal()
    {
        var f = NewFx(); using var _d = f.Db;
        f.Tags.AddTag(f.SectionId, "comedy");
        await f.Vm.LoadAsync(f.SectionId);
        f.Vm.RemoveTagCommand.Execute("comedy");
        f.Vm.Tags.ShouldNotContain("comedy");
        f.Tags.GetTags(f.SectionId).ShouldNotContain("comedy");
    }

    [Fact]
    public async Task TagInput_change_updates_suggestions_excluding_already_applied()
    {
        var f = NewFx(); using var _d = f.Db;
        var src = f.Lib.GetSources()[0];
        var other = f.Lib.UpsertSection(src.Id, "Creator B");
        f.Tags.AddTag(other, "comedy");
        f.Tags.AddTag(other, "comic relief");
        f.Tags.AddTag(f.SectionId, "comedy");      // already applied here
        await f.Vm.LoadAsync(f.SectionId);
        f.Vm.TagInput = "com";
        f.Vm.Suggestions.ShouldContain("comic relief");
        f.Vm.Suggestions.ShouldNotContain("comedy"); // already applied -> excluded
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test VideoShelf.slnx -c Release --nologo -v q --filter "FullyQualifiedName~SectionDetailViewModelTests"`
Expected: FAIL — `SectionDetailViewModel` not defined.

- [ ] **Step 3: Write the implementation**

`SectionDetailViewModel.cs`. If `SeriesViewModel`'s constructor needs more than `(SeriesSummary)` (e.g. an `IThumbnailService`), mirror exactly how `SectionViewModel` builds its `SeriesViewModel`s and inject the same dependencies here.

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.ViewModels;

public sealed partial class SectionDetailViewModel(LibraryRepository library, TagRepository tags) : ObservableObject
{
    public long SectionId { get; private set; }

    [ObservableProperty] private string displayName = "";
    [ObservableProperty] private string tagInput = "";

    public ObservableCollection<SeriesViewModel> SeriesList { get; } = [];
    public ObservableCollection<string> Tags { get; } = [];
    public ObservableCollection<string> Suggestions { get; } = [];

    public event EventHandler<EpisodeView>? PlayRequested;

    public async Task LoadAsync(long sectionId)
    {
        SectionId = sectionId;

        var section = library.GetSections(0).FirstOrDefault(s => s.Id == sectionId);
        // NOTE: GetSections takes a sourceId. If there's no by-id section lookup, add one to
        // LibraryRepository: `Section? GetSection(long id)` and use it here. STOP and report if unsure.
        DisplayName = section?.DisplayName ?? "";

        var summaries = await Task.Run(() => library.GetSeriesSummaries(sectionId));
        SeriesList.Clear();
        foreach (var s in summaries)
        {
            var svm = new SeriesViewModel(s);            // MIRROR SectionViewModel's construction
            svm.PlayRequested += (_, e) => PlayRequested?.Invoke(this, e);
            SeriesList.Add(svm);
        }

        Tags.Clear();
        foreach (var t in await Task.Run(() => tags.GetTags(sectionId))) Tags.Add(t);
        RefreshSuggestions();
    }

    [RelayCommand]
    private void AddTag()
    {
        var norm = TagRepository.Normalize(TagInput);
        if (norm.Length == 0) return;
        tags.AddTag(SectionId, norm);
        if (!Tags.Contains(norm)) Tags.Add(norm);
        TagInput = "";
        RefreshSuggestions();
    }

    [RelayCommand]
    private void RemoveTag(string tag)
    {
        tags.RemoveTag(SectionId, tag);
        Tags.Remove(tag);
        RefreshSuggestions();
    }

    partial void OnTagInputChanged(string value) => RefreshSuggestions();

    private void RefreshSuggestions()
    {
        var query = TagRepository.Normalize(TagInput);
        var applied = new HashSet<string>(Tags);
        var all = tags.GetAllTags()
            .Where(t => !applied.Contains(t))
            .Where(t => query.Length == 0 || t.Contains(query, StringComparison.OrdinalIgnoreCase));
        Suggestions.Clear();
        foreach (var t in all) Suggestions.Add(t);
    }
}
```

> The section-by-id lookup is the one spot likely to need a tiny Core addition. If `LibraryRepository` has no way to fetch a single `Section` by id, add `public Section? GetSection(long id)` (one small query: `SELECT id, source_id, folder_name, display_name FROM sections WHERE id=@id`) with its own Core test in `LibraryRepositoryTests`, then use it here. Do not hack around it with `GetSections(0)`.

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test VideoShelf.slnx -c Release --nologo -v q --filter "FullyQualifiedName~SectionDetailViewModelTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```
git add src/VideoShelf.App/ViewModels/SectionDetailViewModel.cs tests/VideoShelf.App.Tests/SectionDetailViewModelTests.cs
git commit -m "feat(app): add SectionDetailViewModel with tag editor

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

> If you added `GetSection`, include `src/VideoShelf.Core/Storage/LibraryRepository.cs` and the Core test file in this commit (or a preceding one).

---

## Task 7: MainViewModel navigation wiring (App)

**Files:**
- Modify: `src/VideoShelf.App/ViewModels/MainViewModel.cs`
- Test: `tests/VideoShelf.App.Tests/MainViewModelNavigationTests.cs`

Add a top-level Home/Browse/SectionDetail switch. Home is the default landing and loads Discovery on `InitializeAsync`. Discovery's `PlayRequested` → `PlayEpisode`; `SectionOpenRequested` → open section detail; SectionDetail's `PlayRequested` → `PlayEpisode`.

- [ ] **Step 1: Read first, then write the failing test**

Read the current `MainViewModel.cs` fully. Note its constructor signature and `InitializeAsync`. You will **add** `Discovery` and `SectionDetail` to the constructor and members without breaking existing wiring. Read `MainViewModelTests.cs` / `MainViewModelPlaybackTests.cs` for how it's constructed in tests (you must update those constructions if you change the ctor — but prefer adding new optional-free params and fixing call sites).

Create `tests/VideoShelf.App.Tests/MainViewModelNavigationTests.cs`:

```csharp
using Shouldly;
using VideoShelf.App.ViewModels;
using Xunit;

namespace VideoShelf.App.Tests;

public sealed class MainViewModelNavigationTests
{
    [Fact]
    public void Default_view_is_home()
    {
        var vm = MainViewModelTestFactory.Create(out _);
        vm.CurrentView.ShouldBe(AppView.Home);
    }

    [Fact]
    public void ShowBrowse_then_ShowHome_switches_view()
    {
        var vm = MainViewModelTestFactory.Create(out _);
        vm.ShowBrowseCommand.Execute(null);
        vm.CurrentView.ShouldBe(AppView.Browse);
        vm.ShowHomeCommand.Execute(null);
        vm.CurrentView.ShouldBe(AppView.Home);
    }

    [Fact]
    public async Task OpenSection_switches_to_section_detail()
    {
        var vm = MainViewModelTestFactory.Create(out var ctx);
        await vm.OpenSectionAsync(ctx.SectionId);
        vm.CurrentView.ShouldBe(AppView.SectionDetail);
        vm.SectionDetail.SectionId.ShouldBe(ctx.SectionId);
    }

    [Fact]
    public async Task Discovery_section_open_routes_to_section_detail()
    {
        var vm = MainViewModelTestFactory.Create(out var ctx);
        await vm.InitializeAsync();
        vm.Discovery.SectionOpenRequestedRaise(ctx.SectionId); // test helper, see factory note
        vm.CurrentView.ShouldBe(AppView.SectionDetail);
    }
}
```

> Create a small `MainViewModelTestFactory` in `tests/VideoShelf.App.Tests/TestSupport/` that builds a `MainViewModel` over an `AppTempDb` with all real repos + the existing fakes used by current `MainViewModel` tests (copy from `MainViewModelTests.cs`). Have it seed one section (return its id via an `out` context record). For the last test, instead of a private raiser, prefer triggering through a real `SectionCardViewModel.OpenCommand` if reachable; if not, drop that 4th test and rely on `OpenSectionAsync` coverage — do not add production-only test hooks.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test VideoShelf.slnx -c Release --nologo -v q --filter "FullyQualifiedName~MainViewModelNavigationTests"`
Expected: FAIL — `AppView` / `CurrentView` / `ShowBrowseCommand` not defined.

- [ ] **Step 3: Implement the navigation additively**

Add to `MainViewModel.cs` (keep all existing members):

```csharp
public enum AppView { Home, Browse, SectionDetail }
```

In the class, add fields/properties and wire in the constructor (append the two new params to the existing ctor and forward from DI):

```csharp
public DiscoveryViewModel Discovery { get; }
public SectionDetailViewModel SectionDetail { get; }

[ObservableProperty] private AppView currentView = AppView.Home;

// in the constructor body, after existing assignments:
Discovery = discovery;       // new ctor param: DiscoveryViewModel discovery
SectionDetail = sectionDetail; // new ctor param: SectionDetailViewModel sectionDetail
Discovery.PlayRequested += (_, e) => PlayEpisode(e);
Discovery.SectionOpenRequested += async (_, id) => await OpenSectionAsync(id);
SectionDetail.PlayRequested += (_, e) => PlayEpisode(e);

[RelayCommand] private void ShowHome() => CurrentView = AppView.Home;
[RelayCommand] private void ShowBrowse() => CurrentView = AppView.Browse;

public async Task OpenSectionAsync(long sectionId)
{
    await SectionDetail.LoadAsync(sectionId);
    CurrentView = AppView.SectionDetail;
}
```

In `InitializeAsync`, after the existing sources/library load, add Discovery load and default to Home:

```csharp
await Discovery.LoadAsync();
CurrentView = AppView.Home;
```

> If `PlayEpisode` is a method (it is, per the digest: `void PlayEpisode(EpisodeView)`), the lambdas above compile. Update existing `MainViewModel` test constructions and the DI factory (Task 8) to pass the two new args. If the ctor is already long, that's fine — match its style.

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test VideoShelf.slnx -c Release --nologo -v q --filter "FullyQualifiedName~MainViewModelNavigationTests"`
Expected: PASS. Then run all App VM tests to catch ctor-call-site breaks:
Run: `dotnet test VideoShelf.slnx -c Release --nologo -v q --filter "FullyQualifiedName~MainViewModel"`
Expected: PASS.

- [ ] **Step 5: Commit**

```
git add src/VideoShelf.App/ViewModels/MainViewModel.cs tests/VideoShelf.App.Tests/MainViewModelNavigationTests.cs tests/VideoShelf.App.Tests/TestSupport
git commit -m "feat(app): add Home/Browse/SectionDetail navigation to MainViewModel

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 8: Views, shell nav, DI registration (App, integration)

**Files:**
- Create: `src/VideoShelf.App/Views/DiscoveryView.xaml` (+ `DiscoveryView.xaml.cs`)
- Create: `src/VideoShelf.App/Views/SectionDetailView.xaml` (+ `SectionDetailView.xaml.cs`)
- Modify: `src/VideoShelf.App/MainWindow.xaml` — top-level Home/Browse nav + view host
- Modify: `src/VideoShelf.App/ServiceCollectionExtensions.cs` — register new types
- Test: extend `tests/VideoShelf.App.Tests/HostBuildsTests.cs`

Views are integration-only (screenshot-verified in Milestone 6). Keep them **additive** over WPF-UI theming — no restyled control templates.

- [ ] **Step 1: Register services (DI)**

In `ServiceCollectionExtensions.AddVideoShelf`, add registrations next to the existing repos/VMs:

```csharp
services.AddSingleton<TagRepository>();
services.AddSingleton<DiscoveryRepository>();
services.AddSingleton<DiscoveryViewModel>();
services.AddSingleton<SectionDetailViewModel>();
```

`DiscoveryRepository` resolves `VideoShelfDb`, `LibraryRepository`, `TagRepository` (all registered). `MainViewModel` now also needs `DiscoveryViewModel` + `SectionDetailViewModel` — DI resolves them automatically since `MainViewModel` is `AddSingleton` and the container fills ctor params. No factory change needed unless `MainViewModel` uses a manual factory lambda; if it does, append the two new args there.

- [ ] **Step 2: Extend HostBuilds test**

In `HostBuildsTests.cs`, add assertions that the new services resolve:

```csharp
[Fact]
public void Host_resolves_discovery_services()
{
    using var host = TestHost.Build(); // mirror however HostBuildsTests builds the host
    host.Services.GetService(typeof(VideoShelf.Core.Storage.TagRepository)).ShouldNotBeNull();
    host.Services.GetService(typeof(VideoShelf.Core.Discovery.DiscoveryRepository)).ShouldNotBeNull();
    host.Services.GetService(typeof(VideoShelf.App.ViewModels.Discovery.DiscoveryViewModel)).ShouldNotBeNull();
    host.Services.GetService(typeof(VideoShelf.App.ViewModels.SectionDetailViewModel)).ShouldNotBeNull();
}
```

> Mirror the exact host-building call the existing `HostBuildsTests` uses. If it asserts via `GetRequiredService`, match that.

- [ ] **Step 3: Run the host test (red→green for DI)**

Run: `dotnet test VideoShelf.slnx -c Release --nologo -v q --filter "FullyQualifiedName~HostBuildsTests"`
Expected: PASS once registrations are in. (If a ctor param is unregistered, this fails with an Activation error — fix the registration.)

- [ ] **Step 4: Write DiscoveryView.xaml**

Create `Views/DiscoveryView.xaml` — a `UserControl` of vertically stacked horizontal rails bound to `DiscoveryViewModel`. Use plain WPF-UI/standard controls; **do not** re-template themed controls. Each rail hides via a `BooleanToVisibilityConverter` on its `Has*` flag (reuse the converter the app already uses for unwatched badges — check `MainWindow.xaml` resources; if none, add the standard `BooleanToVisibilityConverter` to `App.xaml` resources).

```xml
<UserControl x:Class="VideoShelf.App.Views.DiscoveryView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <ScrollViewer VerticalScrollBarVisibility="Auto">
        <StackPanel Margin="24">

            <TextBlock Text="Nothing here yet — add a source and start watching."
                       Opacity="0.7" Margin="0,40"
                       Visibility="{Binding IsEmpty, Converter={StaticResource BoolToVis}}"/>

            <StackPanel Visibility="{Binding HasContinueWatching, Converter={StaticResource BoolToVis}}">
                <TextBlock Text="Continue watching" FontSize="18" FontWeight="SemiBold" Margin="0,0,0,8"/>
                <ScrollViewer HorizontalScrollBarVisibility="Auto" VerticalScrollBarVisibility="Disabled">
                    <ItemsControl ItemsSource="{Binding ContinueWatching}">
                        <ItemsControl.ItemsPanel>
                            <ItemsPanelTemplate><StackPanel Orientation="Horizontal"/></ItemsPanelTemplate>
                        </ItemsControl.ItemsPanel>
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Button Command="{Binding PlayCommand}" Margin="0,0,12,0" Padding="0"
                                        Background="Transparent" BorderThickness="0" Width="200">
                                    <StackPanel>
                                        <Image Source="{Binding ThumbnailPath}" Height="112" Stretch="UniformToFill"/>
                                        <ProgressBar Minimum="0" Maximum="1" Value="{Binding ProgressFraction}"
                                                     Height="3" Margin="0,2,0,4"/>
                                        <TextBlock Text="{Binding SeriesTitle}" FontWeight="SemiBold" TextTrimming="CharacterEllipsis"/>
                                        <TextBlock Text="{Binding EpisodeLabel}" Opacity="0.7" FontSize="12"/>
                                    </StackPanel>
                                </Button>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </ScrollViewer>
            </StackPanel>

            <StackPanel Margin="0,24,0,0" Visibility="{Binding HasForYou, Converter={StaticResource BoolToVis}}">
                <TextBlock Text="For you" FontSize="18" FontWeight="SemiBold" Margin="0,0,0,8"/>
                <ScrollViewer HorizontalScrollBarVisibility="Auto" VerticalScrollBarVisibility="Disabled">
                    <ItemsControl ItemsSource="{Binding ForYou}">
                        <ItemsControl.ItemsPanel>
                            <ItemsPanelTemplate><StackPanel Orientation="Horizontal"/></ItemsPanelTemplate>
                        </ItemsControl.ItemsPanel>
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Button Command="{Binding OpenCommand}" Margin="0,0,12,0" Width="200"
                                        HorizontalContentAlignment="Left">
                                    <StackPanel>
                                        <TextBlock Text="{Binding DisplayName}" FontWeight="SemiBold" TextTrimming="CharacterEllipsis"/>
                                        <TextBlock Text="{Binding TagsLabel}" Opacity="0.7" FontSize="12" TextTrimming="CharacterEllipsis"/>
                                        <TextBlock Text="{Binding UnwatchedCount, StringFormat='{}{0} unwatched'}"
                                                   Opacity="0.6" FontSize="12"
                                                   Visibility="{Binding HasUnwatched, Converter={StaticResource BoolToVis}}"/>
                                    </StackPanel>
                                </Button>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </ScrollViewer>
            </StackPanel>

            <!-- Recently added rail: same structure as Continue-watching, bound to RecentlyAdded,
                 card uses PlayCommand, no ProgressBar. Title "Recently added", gated on HasRecentlyAdded. -->
            <!-- Recently watched rail: same, bound to RecentlyWatched, gated on HasRecentlyWatched. -->

            <StackPanel Margin="0,24,0,0" Visibility="{Binding HasTags, Converter={StaticResource BoolToVis}}">
                <TextBlock Text="Pick a tag" FontSize="18" FontWeight="SemiBold" Margin="0,0,0,8"/>
                <ItemsControl ItemsSource="{Binding AvailableTags}">
                    <ItemsControl.ItemsPanel>
                        <ItemsPanelTemplate><WrapPanel/></ItemsPanelTemplate>
                    </ItemsControl.ItemsPanel>
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <ToggleButton Content="{Binding Label}" IsChecked="{Binding IsSelected, Mode=OneWay}"
                                          Margin="0,0,8,8"
                                          Command="{Binding DataContext.ToggleTagCommand, RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                                          CommandParameter="{Binding}"/>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
                <ItemsControl ItemsSource="{Binding TagResults}" Margin="0,8,0,0"
                              Visibility="{Binding HasTagResults, Converter={StaticResource BoolToVis}}">
                    <ItemsControl.ItemsPanel>
                        <ItemsPanelTemplate><WrapPanel/></ItemsPanelTemplate>
                    </ItemsControl.ItemsPanel>
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <Button Command="{Binding OpenCommand}" Content="{Binding DisplayName}" Margin="0,0,8,8"/>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </StackPanel>

        </StackPanel>
    </ScrollViewer>
</UserControl>
```

`DiscoveryView.xaml.cs`: standard `InitializeComponent()` only.

> Replace the two commented rails with real copies of the Continue-watching rail (DRY-by-structure is fine in XAML). Confirm the converter key (`BoolToVis`) matches the app's existing one; if the app already defines a converter with a different key, use that key everywhere here.

- [ ] **Step 5: Write SectionDetailView.xaml**

Create `Views/SectionDetailView.xaml` — section name, a tag editor (pills with remove + an autocomplete `TextBox` bound to `TagInput` with a `Suggestions` list + Add button), and the series list (reuse the same series `DataTemplate`/control the browse view uses — check `MainWindow.xaml` for the existing series item presentation and mirror it).

```xml
<UserControl x:Class="VideoShelf.App.Views.SectionDetailView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <ScrollViewer VerticalScrollBarVisibility="Auto">
        <StackPanel Margin="24">
            <TextBlock Text="{Binding DisplayName}" FontSize="24" FontWeight="SemiBold" Margin="0,0,0,12"/>

            <TextBlock Text="Tags" FontWeight="SemiBold" Margin="0,0,0,4"/>
            <ItemsControl ItemsSource="{Binding Tags}">
                <ItemsControl.ItemsPanel>
                    <ItemsPanelTemplate><WrapPanel/></ItemsPanelTemplate>
                </ItemsControl.ItemsPanel>
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <Border Background="#22FFFFFF" CornerRadius="12" Padding="8,2" Margin="0,0,6,6">
                            <StackPanel Orientation="Horizontal">
                                <TextBlock Text="{Binding}" VerticalAlignment="Center"/>
                                <Button Content="✕" Margin="6,0,0,0" Padding="2,0" Background="Transparent" BorderThickness="0"
                                        Command="{Binding DataContext.RemoveTagCommand, RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                                        CommandParameter="{Binding}"/>
                            </StackPanel>
                        </Border>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>

            <StackPanel Orientation="Horizontal" Margin="0,4,0,0">
                <TextBox Width="220" Text="{Binding TagInput, UpdateSourceTrigger=PropertyChanged}"/>
                <Button Content="Add tag" Margin="8,0,0,0" Command="{Binding AddTagCommand}"/>
            </StackPanel>
            <ItemsControl ItemsSource="{Binding Suggestions}" Margin="0,4,0,0">
                <ItemsControl.ItemsPanel>
                    <ItemsPanelTemplate><WrapPanel/></ItemsPanelTemplate>
                </ItemsControl.ItemsPanel>
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <Button Content="{Binding}" Margin="0,0,6,6" Padding="6,2"
                                Command="{Binding DataContext.AddSuggestionCommand, RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                                CommandParameter="{Binding}"/>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>

            <TextBlock Text="Series" FontWeight="SemiBold" Margin="0,20,0,8"/>
            <ItemsControl ItemsSource="{Binding SeriesList}">
                <!-- MIRROR the series item template used in the browse view. -->
            </ItemsControl>
        </StackPanel>
    </ScrollViewer>
</UserControl>
```

> The suggestion-click uses an `AddSuggestionCommand(string)` that isn't in Task 6. Add it to `SectionDetailViewModel`: a `[RelayCommand] private void AddSuggestion(string tag) { TagInput = tag; AddTag(); }` (extract the body of `AddTag` into a private method both commands call). Add a quick unit test `AddSuggestion_adds_and_clears` to `SectionDetailViewModelTests` and keep it green. If you prefer not to expand scope, drop the suggestion `ItemsControl` and keep only type-and-Add — but then remove the dangling command binding.

- [ ] **Step 6: Wire the shell nav in MainWindow.xaml**

In `MainWindow.xaml`, add a top-level nav (two buttons or a WPF-UI `NavigationView`; simplest: a header `StackPanel` with "Home" / "Browse" buttons bound to `ShowHomeCommand`/`ShowBrowseCommand`) and a content host that swaps views by `CurrentView`. Keep the existing browse content; wrap it so it shows only when `CurrentView == Browse`. Use a simple style/trigger or three overlaid panels gated by an `enum→Visibility` converter.

Minimal approach using three hosts gated by an `EnumToVisibilityConverter` (add one in `App.xaml` resources if absent):

```xml
<DockPanel>
    <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Margin="16,8">
        <Button Content="Home" Command="{Binding ShowHomeCommand}" Margin="0,0,8,0"/>
        <Button Content="Browse" Command="{Binding ShowBrowseCommand}"/>
    </StackPanel>

    <Grid>
        <views:DiscoveryView DataContext="{Binding Discovery}"
            Visibility="{Binding DataContext.CurrentView, RelativeSource={RelativeSource AncestorType=Window},
                         Converter={StaticResource EnumToVis}, ConverterParameter=Home}"/>

        <!-- existing browse content here, wrapped: -->
        <ContentControl
            Visibility="{Binding CurrentView, Converter={StaticResource EnumToVis}, ConverterParameter=Browse}">
            <!-- move/keep the current Library browse UI inside this ContentControl -->
        </ContentControl>

        <views:SectionDetailView DataContext="{Binding SectionDetail}"
            Visibility="{Binding DataContext.CurrentView, RelativeSource={RelativeSource AncestorType=Window},
                         Converter={StaticResource EnumToVis}, ConverterParameter=SectionDetail}"/>
    </Grid>
</DockPanel>
```

Add an `EnumToVisibilityConverter` (returns `Visible` when `value.ToString() == parameter`, else `Collapsed`) under `Converters/` and register it in `App.xaml` as `EnumToVis`. Add the `xmlns:views="clr-namespace:VideoShelf.App.Views"` namespace to `MainWindow.xaml`.

> Do not delete the existing browse markup — **move** it inside the gated `ContentControl`. If the player/PiP overlay lives in `MainWindow.xaml`, leave it exactly where it is (it overlays based on `IsInlinePlayerVisible`); the nav host sits beneath it.

- [ ] **Step 7: Build + full suite**

Run: `dotnet test VideoShelf.slnx -c Release --nologo -v q`
Expected: PASS — all prior tests plus the new ones (target ≈ 170+ total). XAML compiles (the build step inside `dotnet test` covers BAML compilation). If a binding references a missing converter key, the build still succeeds but it'll surface in Milestone 6 screenshots — double-check converter keys now.

- [ ] **Step 8: Commit**

```
git add src/VideoShelf.App/Views src/VideoShelf.App/MainWindow.xaml src/VideoShelf.App/App.xaml src/VideoShelf.App/ServiceCollectionExtensions.cs src/VideoShelf.App/Converters src/VideoShelf.App/ViewModels/SectionDetailViewModel.cs tests/VideoShelf.App.Tests/HostBuildsTests.cs tests/VideoShelf.App.Tests/SectionDetailViewModelTests.cs
git commit -m "feat(app): discovery + section-detail views and Home/Browse shell nav

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Final review (controller, before PR)

After all 8 tasks, do a whole-branch review (this is where the Phase-2 cross-thread and Phase-3 off-thread bugs were caught — tests alone missed them):

1. **Cross-thread scan:** grep the branch for `ConfigureAwait(false)` in any App VM that mutates an `ObservableCollection`. There must be none on those chains. The `Task.Run(...)`-then-await pattern in `DiscoveryViewModel.LoadAsync`/`ToggleTag` must resume on the UI context.
2. **Full gate:** `dotnet test VideoShelf.slnx -c Release --nologo -v q` — all green.
3. **No themed-control restyling:** confirm the new XAML only uses additive styling (no `ControlTemplate`/`Style` overrides of WPF-UI controls).
4. **DI completeness:** `HostBuildsTests` resolves the whole graph including the new `MainViewModel` ctor params.
5. **Schema safety:** the `resume_updated_at` migration is guarded/idempotent (running `Migrate()` twice is a no-op) — there should be a `SchemaMigrationTests` case proving idempotency already; if it doesn't cover the new column, add an assertion.

Then push `feat/discovery`, open the PR, `sleep ~20s`, foreground-watch CI (`gh pr checks <PR#> --watch`), merge `--merge --delete-branch` from the repo root, sync `main`, and update `ROADMAP.md` (flip Milestone 4 to ✅ Merged with the PR link + a one-line summary; add any gotchas — e.g. the `resume_updated_at` column and that discovery views remain screenshot-unverified until Milestone 6).

---

## Self-review against the spec (done while writing)

- **Continue-watching (#5):** Task 4 `GetContinueWatching` (resumable, newest-first via `resume_updated_at`) + Task 5 rail. ✅
- **Recently-added / recently-watched (#4):** Task 4 `GetRecentlyAdded`/`GetRecentlyWatched` + Task 5 rails. ✅
- **For-you (taste from history):** Task 3 scoring + Task 4 `GetForYou` (recency-decayed tag affinity, unwatched-weighted, excludes already-watched sections) + Task 5 rail. ✅ (full weighted scoring per the chosen option)
- **Pick-a-tag (multi-select, re-rank, unwatched-weighted):** Task 4 `GetSectionsByTags` + Task 5 chips/results + `ToggleTag`. ✅
- **More-from-section:** Task 4 `GetMoreFromSection`. ✅ (query + DTO ready; surfaced contextually in the section-detail/Phase-6 polish — exposed via repo now, no dedicated rail required by spec)
- **Section tagging UI (section-level only):** Task 1 repo + Task 6 `SectionDetailViewModel` + Task 8 view, reached via the dedicated section-detail view (chosen option). ✅
- **OUT (per spec §13):** no tags on series/videos, no speed/sidecars/streaming/scraping — none added. ✅
- **Home landing + Browse nav (chosen option):** Task 7 + Task 8. ✅
