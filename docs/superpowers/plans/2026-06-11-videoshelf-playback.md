# VideoShelf Playback (Player + PiP) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add fully integrated libVLC playback to VideoShelf — an embedded player View with overlay controls (play/pause, seek, seek-preview thumbnails, volume, fullscreen, embedded subtitle/audio pickers, chapter navigation, frame capture) plus a detachable always-on-top mini-player/PiP — driven by testable view-models that handle resume/continue-watching, auto-mark-watched-on-end, and auto-play-next-episode-within-a-series.

**Architecture:** Mirror the proven Phase-2 thumbnail pattern (abstract interface + thin libVLC concrete + plain testable consumer). An **`IPlaybackEngine`** interface abstracts the libVLC `MediaPlayer` (play/pause, seek, position/length/ended events, volume, audio+subtitle track enumeration/selection, chapter list/navigation, frame snapshot, seek-preview frame generation, dispose). All decision logic lives in plain, unit-tested view-models/services that depend on `IPlaybackEngine` and Core repositories and are exercised with a **fake engine**. The concrete **`LibVlcPlaybackEngine`** (wrapping `LibVLCSharp` `MediaPlayer`), the `VideoView`-hosting player View, and the PiP window are kept THIN and are verification-gated by a Release build only (no unit tests); the Phase 6 harness screenshots them later. Resume/watched/next-episode state is persisted through new Core repository methods over the existing `videos` / `settings` tables (the `videos.resume_position` column already exists from Phase 2).

**Tech Stack:** .NET 10, WPF, WPF-UI (Fluent dark Mica), CommunityToolkit.Mvvm, LibVLCSharp 3.9.7.1 + `LibVLCSharp.WPF` 3.9.7.1 + VideoLAN.LibVLC.Windows 3.0.23.1, Microsoft.Data.Sqlite, xUnit + Shouldly.

---

## Conventions for every task

- **Worktree:** `C:\Agent Projects\VideoShelf\.worktrees\feat-playback` on branch `feat/playback`. Do not switch branches or touch `main`.
- **CWD resets between shell calls** — always `cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback"` at the start of each command.
- **Test gate (all):** `dotnet test VideoShelf.slnx -c Release --nologo -v q`
- **Core tests only:** `dotnet test tests/VideoShelf.Core.Tests/VideoShelf.Core.Tests.csproj -c Release --nologo -v q`
- **App tests only:** `dotnet test tests/VideoShelf.App.Tests/VideoShelf.App.Tests.csproj -c Release --nologo -v q`
- **Baseline before any work:** 71 passing (45 Core + 26 App). Each TDD task adds tests; View/engine tasks add none.
- **TDD loop every code task:** write failing test → run (expect fail) → minimal impl → run (expect pass) → commit.
- **Commit trailer (always):**
  ```
  Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
  ```
  Git author stays the user's identity. Commit small and per-task; do NOT `git push` or open a PR (the controller does that after all tasks).
- **Standing principles:** read-only on video files (playback + snapshots never mutate them; screenshots write ONLY to the app capture folder); SQLite owns metadata; self-contained (libVLC only, no external tools/PATH, no network); crash-safe; fail-safe (engine/snapshot failures surface gracefully, never crash the app).
- **Theming rule:** never override/re-base a WPF-UI themed control's `Style`/`ControlTemplate` for cosmetics — additive only.

---

## Task 0: Verify clean baseline

**Files:** none (verification only).

- [ ] **Step 1: Confirm green baseline**

Run:
```
cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && dotnet test VideoShelf.slnx -c Release --nologo -v q 2>&1 | tail -5
```
Expected: build succeeds, **Passed! - Failed: 0, Passed: 71** (or the run prints two project summaries totalling 71 passed, 0 failed). If red, STOP and report — do not build on a broken base.

---

## Task 1: Core — resume position read/write/clear on `LibraryRepository`

Spec §9 "Resume / continue-watching (#5)": periodically save position to `videos.resume_position`; offer resume on reopen; clear when marked watched. Placement: resume is a property of a `video` row (like `watched`/`missing`), so it lives on `LibraryRepository` alongside the other `videos`-row writers. Marking watched already lives on `WatchRepository.SetWatched`; Task 2 makes that clear resume.

**Files:**
- Modify: `src/VideoShelf.Core/Storage/LibraryRepository.cs`
- Test: `tests/VideoShelf.Core.Tests/Storage/ResumePositionTests.cs` (create)

- [ ] **Step 1: Write the failing test**

Create `tests/VideoShelf.Core.Tests/Storage/ResumePositionTests.cs`:
```csharp
using Shouldly;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;

namespace VideoShelf.Core.Tests.Storage;

public class ResumePositionTests
{
    private static (LibraryRepository lib, long videoId) Seed(TempDb temp)
    {
        var lib = new LibraryRepository(temp.Db);
        var seriesId = lib.UpsertSeries(lib.UpsertSection(lib.UpsertSource(@"C:\V", "V"), "S"), "Base", false);
        var videoId = lib.UpsertVideo(seriesId, @"C:\V\S\a.mp4", 1, ".mp4");
        return (lib, videoId);
    }

    [Fact]
    public void New_video_has_null_resume_position()
    {
        using var temp = new TempDb();
        var (lib, videoId) = Seed(temp);

        lib.GetResumePosition(videoId).ShouldBeNull();
    }

    [Fact]
    public void SetResumePosition_persists_and_is_read_back()
    {
        using var temp = new TempDb();
        var (lib, videoId) = Seed(temp);

        lib.SetResumePosition(videoId, 123.5);

        lib.GetResumePosition(videoId).ShouldBe(123.5);
    }

    [Fact]
    public void SetResumePosition_overwrites_previous_value()
    {
        using var temp = new TempDb();
        var (lib, videoId) = Seed(temp);

        lib.SetResumePosition(videoId, 10.0);
        lib.SetResumePosition(videoId, 42.0);

        lib.GetResumePosition(videoId).ShouldBe(42.0);
    }

    [Fact]
    public void ClearResumePosition_sets_null()
    {
        using var temp = new TempDb();
        var (lib, videoId) = Seed(temp);

        lib.SetResumePosition(videoId, 99.0);
        lib.ClearResumePosition(videoId);

        lib.GetResumePosition(videoId).ShouldBeNull();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && dotnet test tests/VideoShelf.Core.Tests/VideoShelf.Core.Tests.csproj -c Release --nologo -v q 2>&1 | tail -20`
Expected: FAIL — compile error, `LibraryRepository` does not contain `GetResumePosition`/`SetResumePosition`/`ClearResumePosition`.

- [ ] **Step 3: Write minimal implementation**

In `src/VideoShelf.Core/Storage/LibraryRepository.cs`, add these methods inside the class (e.g. after `ClearMissing`):
```csharp
    /// <summary>Returns the saved resume position in seconds, or null if the video has none.</summary>
    public double? GetResumePosition(long videoId)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT resume_position FROM videos WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", videoId);
        var result = cmd.ExecuteScalar();
        return result is null or System.DBNull ? null : (double)result;
    }

    /// <summary>Saves the resume position (seconds) for a video. Overwrites any previous value.</summary>
    public void SetResumePosition(long videoId, double seconds)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE videos SET resume_position = $p WHERE id = $id";
        cmd.Parameters.AddWithValue("$p", seconds);
        cmd.Parameters.AddWithValue("$id", videoId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Clears the resume position (sets it NULL) — used when a video is marked watched or finishes.</summary>
    public void ClearResumePosition(long videoId)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE videos SET resume_position = NULL WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", videoId);
        cmd.ExecuteNonQuery();
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && dotnet test tests/VideoShelf.Core.Tests/VideoShelf.Core.Tests.csproj -c Release --nologo -v q 2>&1 | tail -8`
Expected: PASS — Core test count is now 49 (45 + 4), 0 failed.

- [ ] **Step 5: Commit**

```
cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && git add src/VideoShelf.Core/Storage/LibraryRepository.cs tests/VideoShelf.Core.Tests/Storage/ResumePositionTests.cs && git commit -m "feat(core): resume position read/write/clear on videos

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 2: Core — marking watched clears resume position

Spec §9: "Position is cleared once the video is marked watched." `WatchRepository.SetWatched(videoId, true)` must NULL `resume_position` in the same transaction; `SetWatched(_, false)` leaves it untouched.

**Files:**
- Modify: `src/VideoShelf.Core/Storage/WatchRepository.cs`
- Test: `tests/VideoShelf.Core.Tests/Storage/WatchRepositoryTests.cs` (add cases)

- [ ] **Step 1: Write the failing test**

Append these two tests inside the existing `WatchRepositoryTests` class in `tests/VideoShelf.Core.Tests/Storage/WatchRepositoryTests.cs`:
```csharp
    [Fact]
    public void MarkWatched_clears_resume_position()
    {
        using var temp = new TempDb();
        var videoId = SeedVideo(temp);
        var lib = new LibraryRepository(temp.Db);
        var watch = new WatchRepository(temp.Db);
        lib.SetResumePosition(videoId, 55.0);

        watch.SetWatched(videoId, true);

        lib.GetResumePosition(videoId).ShouldBeNull();
    }

    [Fact]
    public void MarkUnwatched_does_not_touch_resume_position()
    {
        using var temp = new TempDb();
        var videoId = SeedVideo(temp);
        var lib = new LibraryRepository(temp.Db);
        var watch = new WatchRepository(temp.Db);
        lib.SetResumePosition(videoId, 30.0);

        watch.SetWatched(videoId, false);

        lib.GetResumePosition(videoId).ShouldBe(30.0);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && dotnet test tests/VideoShelf.Core.Tests/VideoShelf.Core.Tests.csproj -c Release --nologo -v q 2>&1 | tail -20`
Expected: FAIL — `MarkWatched_clears_resume_position` fails (resume position still 55.0, not null).

- [ ] **Step 3: Write minimal implementation**

In `src/VideoShelf.Core/Storage/WatchRepository.cs`, inside `SetWatched`, within the `if (watched)` block, add a resume-clearing statement so it runs in the same transaction. Replace the existing `if (watched) { ... }` block with:
```csharp
        if (watched)
        {
            using (var ins = conn.CreateCommand())
            {
                ins.CommandText = "INSERT INTO watch_events(video_id, watched_at) VALUES($id, $at)";
                ins.Parameters.AddWithValue("$id", videoId);
                ins.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("o"));
                ins.ExecuteNonQuery();
            }

            using var clr = conn.CreateCommand();
            clr.CommandText = "UPDATE videos SET resume_position = NULL WHERE id = $id";
            clr.Parameters.AddWithValue("$id", videoId);
            clr.ExecuteNonQuery();
        }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && dotnet test tests/VideoShelf.Core.Tests/VideoShelf.Core.Tests.csproj -c Release --nologo -v q 2>&1 | tail -8`
Expected: PASS — Core test count is now 51, 0 failed.

- [ ] **Step 5: Commit**

```
cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && git add src/VideoShelf.Core/Storage/WatchRepository.cs tests/VideoShelf.Core.Tests/Storage/WatchRepositoryTests.cs && git commit -m "feat(core): clear resume position when a video is marked watched

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 3: Core — `GetNextEpisode` for auto-play next within a series

Spec §9 "Auto-play next episode (#10)": within a series, reaching the end auto-advances to the next episode by `episode_no`; standalones never advance; whole-library chaining is out of scope. Add a query that returns the next `EpisodeView` after a given episode number in a series, or null if none / if the series is a standalone.

**Files:**
- Modify: `src/VideoShelf.Core/Storage/LibraryRepository.cs`
- Test: `tests/VideoShelf.Core.Tests/Storage/NextEpisodeTests.cs` (create)

- [ ] **Step 1: Write the failing test**

Create `tests/VideoShelf.Core.Tests/Storage/NextEpisodeTests.cs`:
```csharp
using Shouldly;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;

namespace VideoShelf.Core.Tests.Storage;

public class NextEpisodeTests
{
    private static (LibraryRepository lib, long seriesId) SeedSeries(TempDb temp, bool standalone)
    {
        var lib = new LibraryRepository(temp.Db);
        var sectionId = lib.UpsertSection(lib.UpsertSource(@"C:\V", "V"), "S");
        var seriesId = lib.UpsertSeries(sectionId, "Base", standalone);
        return (lib, seriesId);
    }

    [Fact]
    public void GetNextEpisode_returns_next_by_episode_no()
    {
        using var temp = new TempDb();
        var (lib, seriesId) = SeedSeries(temp, standalone: false);
        lib.UpsertVideo(seriesId, @"C:\V\S\a.mp4", 1, ".mp4");
        lib.UpsertVideo(seriesId, @"C:\V\S\b.mp4", 2, ".mp4");

        var next = lib.GetNextEpisode(seriesId, 1);

        next.ShouldNotBeNull();
        next!.EpisodeNo.ShouldBe(2);
        next.FilePath.ShouldBe(@"C:\V\S\b.mp4");
    }

    [Fact]
    public void GetNextEpisode_returns_null_at_last_episode()
    {
        using var temp = new TempDb();
        var (lib, seriesId) = SeedSeries(temp, standalone: false);
        lib.UpsertVideo(seriesId, @"C:\V\S\a.mp4", 1, ".mp4");
        lib.UpsertVideo(seriesId, @"C:\V\S\b.mp4", 2, ".mp4");

        lib.GetNextEpisode(seriesId, 2).ShouldBeNull();
    }

    [Fact]
    public void GetNextEpisode_returns_null_for_standalone_series()
    {
        using var temp = new TempDb();
        var (lib, seriesId) = SeedSeries(temp, standalone: true);
        lib.UpsertVideo(seriesId, @"C:\V\S\only.mp4", 1, ".mp4");

        lib.GetNextEpisode(seriesId, 1).ShouldBeNull();
    }

    [Fact]
    public void GetNextEpisode_skips_gaps_in_numbering()
    {
        using var temp = new TempDb();
        var (lib, seriesId) = SeedSeries(temp, standalone: false);
        lib.UpsertVideo(seriesId, @"C:\V\S\a.mp4", 1, ".mp4");
        lib.UpsertVideo(seriesId, @"C:\V\S\c.mp4", 5, ".mp4");

        var next = lib.GetNextEpisode(seriesId, 1);

        next.ShouldNotBeNull();
        next!.EpisodeNo.ShouldBe(5);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && dotnet test tests/VideoShelf.Core.Tests/VideoShelf.Core.Tests.csproj -c Release --nologo -v q 2>&1 | tail -20`
Expected: FAIL — `LibraryRepository` does not contain `GetNextEpisode`.

- [ ] **Step 3: Write minimal implementation**

In `src/VideoShelf.Core/Storage/LibraryRepository.cs`, add this method (e.g. after `GetEpisodes`). It returns an `EpisodeView` (already defined in `BrowseModels.cs`) and mirrors the title-derivation used by `GetEpisodes`:
```csharp
    /// <summary>Returns the next episode after <paramref name="currentEpisodeNo"/> in a series
    /// (ordered by episode_no), or null if there is none or the series is a standalone.</summary>
    public EpisodeView? GetNextEpisode(long seriesId, int currentEpisodeNo)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT v.id, v.series_id, v.file_path, v.episode_no, se.base_title, v.watched, v.missing
            FROM videos v
            JOIN series se ON se.id = v.series_id
            WHERE v.series_id = $s AND se.is_standalone = 0 AND v.episode_no > $e
            ORDER BY v.episode_no
            LIMIT 1
            """;
        cmd.Parameters.AddWithValue("$s", seriesId);
        cmd.Parameters.AddWithValue("$e", currentEpisodeNo);
        using var r = cmd.ExecuteReader();
        if (!r.Read())
            return null;

        var episodeNo = r.GetInt32(3);
        var baseTitle = r.GetString(4);
        var title = episodeNo <= 1 ? baseTitle : $"{baseTitle} {episodeNo}";
        return new EpisodeView(
            r.GetInt64(0), r.GetInt64(1), r.GetString(2), episodeNo, title,
            r.GetInt64(5) != 0, r.GetInt64(6) != 0);
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && dotnet test tests/VideoShelf.Core.Tests/VideoShelf.Core.Tests.csproj -c Release --nologo -v q 2>&1 | tail -8`
Expected: PASS — Core test count is now 55, 0 failed.

- [ ] **Step 5: Commit**

```
cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && git add src/VideoShelf.Core/Storage/LibraryRepository.cs tests/VideoShelf.Core.Tests/Storage/NextEpisodeTests.cs && git commit -m "feat(core): GetNextEpisode for in-series auto-advance

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 4: Core — `SettingsRepository` over the `settings` table (auto-advance toggle)

Spec §9: "Auto-advance can be turned off in settings." The `settings` table (`key TEXT PRIMARY KEY, value TEXT`) already exists. Add a typed repository with a generic key/value get/set plus a strongly-typed `GetAutoAdvanceEpisodes()` / `SetAutoAdvanceEpisodes(bool)` defaulting to **true**.

**Files:**
- Create: `src/VideoShelf.Core/Storage/SettingsRepository.cs`
- Test: `tests/VideoShelf.Core.Tests/Storage/SettingsRepositoryTests.cs` (create)

- [ ] **Step 1: Write the failing test**

Create `tests/VideoShelf.Core.Tests/Storage/SettingsRepositoryTests.cs`:
```csharp
using Shouldly;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;

namespace VideoShelf.Core.Tests.Storage;

public class SettingsRepositoryTests
{
    [Fact]
    public void AutoAdvance_defaults_to_true_when_unset()
    {
        using var temp = new TempDb();
        var settings = new SettingsRepository(temp.Db);

        settings.GetAutoAdvanceEpisodes().ShouldBeTrue();
    }

    [Fact]
    public void AutoAdvance_roundtrips_false()
    {
        using var temp = new TempDb();
        var settings = new SettingsRepository(temp.Db);

        settings.SetAutoAdvanceEpisodes(false);

        settings.GetAutoAdvanceEpisodes().ShouldBeFalse();
    }

    [Fact]
    public void AutoAdvance_roundtrips_back_to_true()
    {
        using var temp = new TempDb();
        var settings = new SettingsRepository(temp.Db);

        settings.SetAutoAdvanceEpisodes(false);
        settings.SetAutoAdvanceEpisodes(true);

        settings.GetAutoAdvanceEpisodes().ShouldBeTrue();
    }

    [Fact]
    public void GetString_returns_fallback_when_key_missing()
    {
        using var temp = new TempDb();
        var settings = new SettingsRepository(temp.Db);

        settings.GetString("nope", "fallback").ShouldBe("fallback");
    }

    [Fact]
    public void SetString_then_GetString_roundtrips()
    {
        using var temp = new TempDb();
        var settings = new SettingsRepository(temp.Db);

        settings.SetString("k", "v");

        settings.GetString("k", "fallback").ShouldBe("v");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && dotnet test tests/VideoShelf.Core.Tests/VideoShelf.Core.Tests.csproj -c Release --nologo -v q 2>&1 | tail -20`
Expected: FAIL — type `SettingsRepository` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `src/VideoShelf.Core/Storage/SettingsRepository.cs`:
```csharp
namespace VideoShelf.Core.Storage;

/// <summary>Typed access to the app's key/value <c>settings</c> table.</summary>
public sealed class SettingsRepository(VideoShelfDb db)
{
    public const string AutoAdvanceKey = "auto_advance_episodes";

    public string GetString(string key, string fallback)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM settings WHERE key = $k";
        cmd.Parameters.AddWithValue("$k", key);
        var result = cmd.ExecuteScalar();
        return result is string s ? s : fallback;
    }

    public void SetString(string key, string value)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO settings(key, value) VALUES($k, $v)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value
            """;
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Whether reaching the end of an episode auto-advances to the next in its series. Default true.</summary>
    public bool GetAutoAdvanceEpisodes()
        => GetString(AutoAdvanceKey, "true") != "false";

    public void SetAutoAdvanceEpisodes(bool value)
        => SetString(AutoAdvanceKey, value ? "true" : "false");
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && dotnet test tests/VideoShelf.Core.Tests/VideoShelf.Core.Tests.csproj -c Release --nologo -v q 2>&1 | tail -8`
Expected: PASS — Core test count is now 60, 0 failed.

- [ ] **Step 5: Commit**

```
cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && git add src/VideoShelf.Core/Storage/SettingsRepository.cs tests/VideoShelf.Core.Tests/Storage/SettingsRepositoryTests.cs && git commit -m "feat(core): SettingsRepository with auto-advance toggle (default true)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 5: App — `IPlaybackEngine` abstraction + supporting record types

Defines the testable seam between view-models and libVLC. No libVLC reference here — pure interface + DTOs so it can be faked. Mirrors the `IThumbnailSnapshotter` pattern (fail-safe, never throw into the UI).

**Files:**
- Create: `src/VideoShelf.App/Services/IPlaybackEngine.cs`
- Test: `tests/VideoShelf.App.Tests/PlaybackEngineContractTests.cs` (create — compile-time contract + DTO behavior)

- [ ] **Step 1: Write the failing test**

Create `tests/VideoShelf.App.Tests/PlaybackEngineContractTests.cs`:
```csharp
using Shouldly;
using VideoShelf.App.Services;

namespace VideoShelf.App.Tests;

public class PlaybackEngineContractTests
{
    [Fact]
    public void TrackOption_carries_id_and_label()
    {
        var t = new TrackOption(2, "English");

        t.Id.ShouldBe(2);
        t.Label.ShouldBe("English");
    }

    [Fact]
    public void SubtitlesOff_is_the_well_known_disabled_id()
    {
        // libVLC uses -1 for "no subtitle track".
        TrackOption.SubtitlesOffId.ShouldBe(-1);
    }

    [Fact]
    public void ChapterOption_carries_index_and_name()
    {
        var c = new ChapterOption(0, "Intro");

        c.Index.ShouldBe(0);
        c.Name.ShouldBe("Intro");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && dotnet test tests/VideoShelf.App.Tests/VideoShelf.App.Tests.csproj -c Release --nologo -v q 2>&1 | tail -20`
Expected: FAIL — types `TrackOption`, `ChapterOption`, `IPlaybackEngine` do not exist.

- [ ] **Step 3: Write minimal implementation**

Create `src/VideoShelf.App/Services/IPlaybackEngine.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace VideoShelf.App.Services;

/// <summary>An audio or subtitle track choice. Id is the libVLC track id; -1 means "subtitles off".</summary>
public sealed record TrackOption(int Id, string Label)
{
    public const int SubtitlesOffId = -1;
}

/// <summary>An embedded chapter. Index is the libVLC chapter index (0-based); Name may be empty.</summary>
public sealed record ChapterOption(int Index, string Name);

/// <summary>
/// Abstracts the libVLC MediaPlayer so all playback decision logic stays unit-testable.
/// Implementations MUST be fail-safe — surface failures via events/return values, never throw into callers.
/// All position/length values are in seconds.
/// </summary>
public interface IPlaybackEngine : IDisposable
{
    // ----- transport -----
    void Load(string filePath);
    void Play();
    void Pause();
    void Stop();
    bool IsPlaying { get; }

    /// <summary>Current playback position in seconds.</summary>
    double Position { get; }
    /// <summary>Media length in seconds (0 until known).</summary>
    double Length { get; }
    void SeekTo(double seconds);

    /// <summary>0..100.</summary>
    int Volume { get; set; }

    // ----- tracks -----
    IReadOnlyList<TrackOption> GetAudioTracks();
    int GetCurrentAudioTrack();
    void SetAudioTrack(int id);

    /// <summary>Subtitle tracks INCLUDING the "subtitles off" option (id == TrackOption.SubtitlesOffId).</summary>
    IReadOnlyList<TrackOption> GetSubtitleTracks();
    int GetCurrentSubtitleTrack();
    void SetSubtitleTrack(int id);

    // ----- chapters -----
    IReadOnlyList<ChapterOption> GetChapters();
    void NextChapter();
    void PreviousChapter();

    // ----- frame capture -----
    /// <summary>Saves the current frame to a PNG. Returns false on any failure (fail-safe).</summary>
    bool TrySnapshot(string outputPngPath);
    /// <summary>Renders a preview frame for the given position to a PNG (for seek-preview). Returns false on failure.</summary>
    Task<bool> TryGeneratePreviewFrameAsync(double seconds, string outputPngPath, CancellationToken cancellationToken);

    // ----- events -----
    /// <summary>Fires (roughly per second) with the current position in seconds.</summary>
    event EventHandler<double>? PositionChanged;
    /// <summary>Fires once when the media length becomes known, with the length in seconds.</summary>
    event EventHandler<double>? LengthChanged;
    /// <summary>Fires when playback reaches the natural end of the media.</summary>
    event EventHandler? Ended;
    /// <summary>Fires when the engine hits an unrecoverable error for the loaded media.</summary>
    event EventHandler? EncounteredError;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && dotnet test tests/VideoShelf.App.Tests/VideoShelf.App.Tests.csproj -c Release --nologo -v q 2>&1 | tail -8`
Expected: PASS — App test count is now 29 (26 + 3), 0 failed.

- [ ] **Step 5: Commit**

```
cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && git add src/VideoShelf.App/Services/IPlaybackEngine.cs tests/VideoShelf.App.Tests/PlaybackEngineContractTests.cs && git commit -m "feat(app): IPlaybackEngine abstraction + track/chapter DTOs

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 6: App tests — `FakePlaybackEngine` test double

A controllable in-memory engine so every later view-model task can drive playback deterministically. Lives in test support (not production).

**Files:**
- Create: `tests/VideoShelf.App.Tests/TestSupport/FakePlaybackEngine.cs`
- Test: `tests/VideoShelf.App.Tests/FakePlaybackEngineTests.cs` (create — proves the fake behaves)

- [ ] **Step 1: Write the failing test**

Create `tests/VideoShelf.App.Tests/FakePlaybackEngineTests.cs`:
```csharp
using Shouldly;
using VideoShelf.App.Services;
using VideoShelf.App.Tests.TestSupport;

namespace VideoShelf.App.Tests;

public class FakePlaybackEngineTests
{
    [Fact]
    public void Load_then_Play_sets_playing()
    {
        var engine = new FakePlaybackEngine();
        engine.Load(@"C:\V\a.mp4");
        engine.Play();

        engine.IsPlaying.ShouldBeTrue();
        engine.LoadedPath.ShouldBe(@"C:\V\a.mp4");
    }

    [Fact]
    public void RaisePosition_fires_PositionChanged_and_updates_Position()
    {
        var engine = new FakePlaybackEngine();
        double seen = -1;
        engine.PositionChanged += (_, p) => seen = p;

        engine.RaisePosition(12.0);

        seen.ShouldBe(12.0);
        engine.Position.ShouldBe(12.0);
    }

    [Fact]
    public void RaiseEnded_fires_Ended()
    {
        var engine = new FakePlaybackEngine();
        var fired = false;
        engine.Ended += (_, _) => fired = true;

        engine.RaiseEnded();

        fired.ShouldBeTrue();
    }

    [Fact]
    public void SetSubtitleTrack_records_selection()
    {
        var engine = new FakePlaybackEngine();
        engine.SetSubtitleTrack(TrackOption.SubtitlesOffId);

        engine.GetCurrentSubtitleTrack().ShouldBe(TrackOption.SubtitlesOffId);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && dotnet test tests/VideoShelf.App.Tests/VideoShelf.App.Tests.csproj -c Release --nologo -v q 2>&1 | tail -20`
Expected: FAIL — type `FakePlaybackEngine` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `tests/VideoShelf.App.Tests/TestSupport/FakePlaybackEngine.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VideoShelf.App.Services;

namespace VideoShelf.App.Tests.TestSupport;

/// <summary>An in-memory IPlaybackEngine for deterministic view-model tests.
/// Tests drive it via the Raise* helpers and read the recorded state.</summary>
public sealed class FakePlaybackEngine : IPlaybackEngine
{
    public string? LoadedPath { get; private set; }
    public bool IsPlaying { get; private set; }
    public bool Disposed { get; private set; }
    public int SnapshotCount { get; private set; }
    public bool SnapshotShouldFail { get; set; }
    public List<double> Seeks { get; } = new();
    public int NextChapterCalls { get; private set; }
    public int PreviousChapterCalls { get; private set; }

    public double Position { get; private set; }
    public double Length { get; set; }
    public int Volume { get; set; } = 100;

    public List<TrackOption> AudioTracks { get; } = new();
    public List<TrackOption> SubtitleTracks { get; } = new();
    public List<ChapterOption> Chapters { get; } = new();

    private int _currentAudio = -1;
    private int _currentSubtitle = TrackOption.SubtitlesOffId;

    public void Load(string filePath) { LoadedPath = filePath; }
    public void Play() => IsPlaying = true;
    public void Pause() => IsPlaying = false;
    public void Stop() => IsPlaying = false;
    public void SeekTo(double seconds) { Position = seconds; Seeks.Add(seconds); }

    public IReadOnlyList<TrackOption> GetAudioTracks() => AudioTracks;
    public int GetCurrentAudioTrack() => _currentAudio;
    public void SetAudioTrack(int id) => _currentAudio = id;

    public IReadOnlyList<TrackOption> GetSubtitleTracks() => SubtitleTracks;
    public int GetCurrentSubtitleTrack() => _currentSubtitle;
    public void SetSubtitleTrack(int id) => _currentSubtitle = id;

    public IReadOnlyList<ChapterOption> GetChapters() => Chapters;
    public void NextChapter() => NextChapterCalls++;
    public void PreviousChapter() => PreviousChapterCalls++;

    public bool TrySnapshot(string outputPngPath)
    {
        SnapshotCount++;
        return !SnapshotShouldFail;
    }

    public Task<bool> TryGeneratePreviewFrameAsync(double seconds, string outputPngPath, CancellationToken cancellationToken)
        => Task.FromResult(!SnapshotShouldFail);

    public event EventHandler<double>? PositionChanged;
    public event EventHandler<double>? LengthChanged;
    public event EventHandler? Ended;
    public event EventHandler? EncounteredError;

    // ----- test drivers -----
    public void RaisePosition(double seconds) { Position = seconds; PositionChanged?.Invoke(this, seconds); }
    public void RaiseLength(double seconds) { Length = seconds; LengthChanged?.Invoke(this, seconds); }
    public void RaiseEnded() { IsPlaying = false; Ended?.Invoke(this, EventArgs.Empty); }
    public void RaiseError() => EncounteredError?.Invoke(this, EventArgs.Empty);

    public void Dispose() => Disposed = true;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && dotnet test tests/VideoShelf.App.Tests/VideoShelf.App.Tests.csproj -c Release --nologo -v q 2>&1 | tail -8`
Expected: PASS — App test count is now 33, 0 failed.

- [ ] **Step 5: Commit**

```
cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && git add tests/VideoShelf.App.Tests/TestSupport/FakePlaybackEngine.cs tests/VideoShelf.App.Tests/FakePlaybackEngineTests.cs && git commit -m "test(app): FakePlaybackEngine test double for player view-models

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 7: App — `ResumePolicy` (throttle + threshold logic)

Spec §9: persist position every ~5s and on pause/stop; offer resume on open; auto-mark-watched near the end clears resume. Isolate the *timing/threshold* rules in a pure, clock-injected helper so they are unit-testable without a real timer. (The view-model in Task 8 calls into this.)

**Files:**
- Create: `src/VideoShelf.App/Services/ResumePolicy.cs`
- Test: `tests/VideoShelf.App.Tests/ResumePolicyTests.cs` (create)

- [ ] **Step 1: Write the failing test**

Create `tests/VideoShelf.App.Tests/ResumePolicyTests.cs`:
```csharp
using Shouldly;
using VideoShelf.App.Services;

namespace VideoShelf.App.Tests;

public class ResumePolicyTests
{
    [Fact]
    public void ShouldSave_false_before_interval_elapses()
    {
        var p = new ResumePolicy();
        p.ShouldSaveOnTick(lastSavedAtSeconds: 10.0, currentSeconds: 13.0).ShouldBeFalse();
    }

    [Fact]
    public void ShouldSave_true_once_interval_elapses()
    {
        var p = new ResumePolicy();
        p.ShouldSaveOnTick(lastSavedAtSeconds: 10.0, currentSeconds: 15.0).ShouldBeTrue();
    }

    [Fact]
    public void IsNearEnd_true_within_completion_window()
    {
        var p = new ResumePolicy();
        // 98% through a 100s video → treated as finished.
        p.IsNearEnd(currentSeconds: 98.0, lengthSeconds: 100.0).ShouldBeTrue();
    }

    [Fact]
    public void IsNearEnd_false_mid_video()
    {
        var p = new ResumePolicy();
        p.IsNearEnd(currentSeconds: 40.0, lengthSeconds: 100.0).ShouldBeFalse();
    }

    [Fact]
    public void IsNearEnd_false_when_length_unknown()
    {
        var p = new ResumePolicy();
        p.IsNearEnd(currentSeconds: 40.0, lengthSeconds: 0.0).ShouldBeFalse();
    }

    [Fact]
    public void ShouldOfferResume_false_for_trivial_position()
    {
        var p = new ResumePolicy();
        // < the minimum meaningful resume position.
        p.ShouldOfferResume(savedSeconds: 2.0, lengthSeconds: 100.0).ShouldBeFalse();
    }

    [Fact]
    public void ShouldOfferResume_false_when_saved_is_near_end()
    {
        var p = new ResumePolicy();
        p.ShouldOfferResume(savedSeconds: 99.0, lengthSeconds: 100.0).ShouldBeFalse();
    }

    [Fact]
    public void ShouldOfferResume_true_for_meaningful_midpoint()
    {
        var p = new ResumePolicy();
        p.ShouldOfferResume(savedSeconds: 50.0, lengthSeconds: 100.0).ShouldBeTrue();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && dotnet test tests/VideoShelf.App.Tests/VideoShelf.App.Tests.csproj -c Release --nologo -v q 2>&1 | tail -20`
Expected: FAIL — type `ResumePolicy` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `src/VideoShelf.App/Services/ResumePolicy.cs`:
```csharp
namespace VideoShelf.App.Services;

/// <summary>Pure timing/threshold rules for resume persistence and completion detection.
/// Kept separate from the player view-model so the rules are unit-testable without a timer.</summary>
public sealed class ResumePolicy
{
    /// <summary>Persist the resume position at most this often during playback.</summary>
    public double SaveIntervalSeconds { get; init; } = 5.0;

    /// <summary>Fraction of the media considered "finished" (auto-mark watched, clear resume).</summary>
    public double CompletionFraction { get; init; } = 0.97;

    /// <summary>Positions below this are too trivial to bother resuming from.</summary>
    public double MinResumeSeconds { get; init; } = 5.0;

    public bool ShouldSaveOnTick(double lastSavedAtSeconds, double currentSeconds)
        => currentSeconds - lastSavedAtSeconds >= SaveIntervalSeconds;

    public bool IsNearEnd(double currentSeconds, double lengthSeconds)
        => lengthSeconds > 0 && currentSeconds >= lengthSeconds * CompletionFraction;

    public bool ShouldOfferResume(double savedSeconds, double lengthSeconds)
        => savedSeconds >= MinResumeSeconds && !IsNearEnd(savedSeconds, lengthSeconds);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && dotnet test tests/VideoShelf.App.Tests/VideoShelf.App.Tests.csproj -c Release --nologo -v q 2>&1 | tail -8`
Expected: PASS — App test count is now 41, 0 failed.

- [ ] **Step 5: Commit**

```
cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && git add src/VideoShelf.App/Services/ResumePolicy.cs tests/VideoShelf.App.Tests/ResumePolicyTests.cs && git commit -m "feat(app): ResumePolicy timing/threshold rules

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 8: App — `PlayerViewModel` core transport, resume-save, and resume-on-open

The central testable player view-model. Depends on `IPlaybackEngine`, `LibraryRepository`, `WatchRepository`, `SettingsRepository`, and `ResumePolicy`. This task covers: load + offer-resume, position ticking → throttled resume-save, pause/stop flush. (Tracks, chapters, end-of-media, auto-next, and screenshot are added in Tasks 9–12 to keep steps bite-sized.)

**Files:**
- Create: `src/VideoShelf.App/ViewModels/PlayerViewModel.cs`
- Test: `tests/VideoShelf.App.Tests/PlayerViewModelTests.cs` (create)

- [ ] **Step 1: Write the failing test**

Create `tests/VideoShelf.App.Tests/PlayerViewModelTests.cs`:
```csharp
using Shouldly;
using VideoShelf.App.Services;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.Tests;

public class PlayerViewModelTests
{
    private static (LibraryRepository lib, WatchRepository watch, SettingsRepository settings, EpisodeView ep)
        Seed(AppTempDb temp, double? resume = null, int episodeNo = 1)
    {
        var lib = new LibraryRepository(temp.Db);
        var sectionId = lib.UpsertSection(lib.UpsertSource(@"C:\V", "V"), "S");
        var seriesId = lib.UpsertSeries(sectionId, "Base", false);
        var path = $@"C:\V\S\e{episodeNo}.mp4";
        var videoId = lib.UpsertVideo(seriesId, path, episodeNo, ".mp4");
        if (resume is { } r) lib.SetResumePosition(videoId, r);
        var ep = new EpisodeView(videoId, seriesId, path, episodeNo, "Base", Watched: false, Missing: false);
        return (lib, new WatchRepository(temp.Db), new SettingsRepository(temp.Db), ep);
    }

    private static PlayerViewModel NewVm(AppTempDb temp, FakePlaybackEngine engine,
        LibraryRepository lib, WatchRepository watch, SettingsRepository settings)
        => new(engine, lib, watch, settings, new ResumePolicy());

    [Fact]
    public void Open_loads_path_into_engine_and_plays()
    {
        using var temp = new AppTempDb();
        var (lib, watch, settings, ep) = Seed(temp);
        var engine = new FakePlaybackEngine();
        var vm = NewVm(temp, engine, lib, watch, settings);

        vm.Open(ep);

        engine.LoadedPath.ShouldBe(ep.FilePath);
        engine.IsPlaying.ShouldBeTrue();
    }

    [Fact]
    public void Open_with_resumable_position_sets_ResumeOffer()
    {
        using var temp = new AppTempDb();
        var (lib, watch, settings, ep) = Seed(temp, resume: 50.0);
        var engine = new FakePlaybackEngine();
        var vm = NewVm(temp, engine, lib, watch, settings);

        vm.Open(ep);
        engine.RaiseLength(100.0); // length needed for the resume threshold check

        vm.CanResume.ShouldBeTrue();
        vm.ResumePositionSeconds.ShouldBe(50.0);
    }

    [Fact]
    public void ResumeCommand_seeks_to_saved_position()
    {
        using var temp = new AppTempDb();
        var (lib, watch, settings, ep) = Seed(temp, resume: 50.0);
        var engine = new FakePlaybackEngine();
        var vm = NewVm(temp, engine, lib, watch, settings);

        vm.Open(ep);
        engine.RaiseLength(100.0);
        vm.ResumeCommand.Execute(null);

        engine.Seeks.ShouldContain(50.0);
        vm.CanResume.ShouldBeFalse();
    }

    [Fact]
    public void Position_tick_saves_resume_after_interval()
    {
        using var temp = new AppTempDb();
        var (lib, watch, settings, ep) = Seed(temp);
        var engine = new FakePlaybackEngine();
        var vm = NewVm(temp, engine, lib, watch, settings);
        vm.Open(ep);
        engine.RaiseLength(100.0);

        engine.RaisePosition(3.0);   // below 5s interval since 0 → no save
        lib.GetResumePosition(ep.VideoId).ShouldBeNull();

        engine.RaisePosition(6.0);   // crosses the 5s interval → save
        lib.GetResumePosition(ep.VideoId).ShouldBe(6.0);
    }

    [Fact]
    public void TogglePlayPause_flushes_resume_position()
    {
        using var temp = new AppTempDb();
        var (lib, watch, settings, ep) = Seed(temp);
        var engine = new FakePlaybackEngine();
        var vm = NewVm(temp, engine, lib, watch, settings);
        vm.Open(ep);
        engine.RaiseLength(100.0);
        engine.RaisePosition(2.0); // below interval, not yet saved

        vm.TogglePlayPauseCommand.Execute(null); // pause → flush

        engine.IsPlaying.ShouldBeFalse();
        lib.GetResumePosition(ep.VideoId).ShouldBe(2.0);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && dotnet test tests/VideoShelf.App.Tests/VideoShelf.App.Tests.csproj -c Release --nologo -v q 2>&1 | tail -20`
Expected: FAIL — type `PlayerViewModel` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `src/VideoShelf.App/ViewModels/PlayerViewModel.cs`:
```csharp
using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoShelf.App.Services;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.ViewModels;

/// <summary>
/// Testable player logic: transport, resume save/offer. Depends only on IPlaybackEngine + Core repos,
/// so it is fully unit-testable with a FakePlaybackEngine. The View binds to it and hosts the VideoView.
/// </summary>
public sealed partial class PlayerViewModel(
    IPlaybackEngine engine,
    LibraryRepository library,
    WatchRepository watch,
    SettingsRepository settings,
    ResumePolicy resumePolicy) : ObservableObject
{
    private EpisodeView? _current;
    private double _lastSavedAt;
    private double _length;

    public IPlaybackEngine Engine => engine;

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private double _positionSeconds;

    [ObservableProperty]
    private double _lengthSeconds;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private bool _canResume;

    [ObservableProperty]
    private double _resumePositionSeconds;

    /// <summary>Loads an episode, starts playback, and prepares a resume offer if one applies.</summary>
    public void Open(EpisodeView episode)
    {
        _current = episode;
        _lastSavedAt = 0;
        _length = 0;
        Title = episode.Title;
        CanResume = false;
        ResumePositionSeconds = library.GetResumePosition(episode.VideoId) ?? 0;

        engine.PositionChanged -= OnPositionChanged;
        engine.LengthChanged -= OnLengthChanged;
        engine.PositionChanged += OnPositionChanged;
        engine.LengthChanged += OnLengthChanged;

        engine.Load(episode.FilePath);
        engine.Play();
        IsPlaying = true;
    }

    private void OnLengthChanged(object? sender, double seconds)
    {
        _length = seconds;
        LengthSeconds = seconds;
        if (_current is { } cur)
        {
            var saved = library.GetResumePosition(cur.VideoId) ?? 0;
            CanResume = resumePolicy.ShouldOfferResume(saved, seconds);
            ResumePositionSeconds = saved;
        }
    }

    private void OnPositionChanged(object? sender, double seconds)
    {
        PositionSeconds = seconds;
        if (_current is { } cur && resumePolicy.ShouldSaveOnTick(_lastSavedAt, seconds))
        {
            library.SetResumePosition(cur.VideoId, seconds);
            _lastSavedAt = seconds;
        }
    }

    [RelayCommand]
    private void Resume()
    {
        engine.SeekTo(ResumePositionSeconds);
        CanResume = false;
    }

    [RelayCommand]
    private void TogglePlayPause()
    {
        if (engine.IsPlaying)
        {
            engine.Pause();
            IsPlaying = false;
            FlushResume();
        }
        else
        {
            engine.Play();
            IsPlaying = true;
        }
    }

    /// <summary>Persists the current position immediately (on pause/stop/close).</summary>
    public void FlushResume()
    {
        if (_current is { } cur)
        {
            library.SetResumePosition(cur.VideoId, PositionSeconds);
            _lastSavedAt = PositionSeconds;
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && dotnet test tests/VideoShelf.App.Tests/VideoShelf.App.Tests.csproj -c Release --nologo -v q 2>&1 | tail -8`
Expected: PASS — App test count is now 46, 0 failed.

- [ ] **Step 5: Commit**

```
cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && git add src/VideoShelf.App/ViewModels/PlayerViewModel.cs tests/VideoShelf.App.Tests/PlayerViewModelTests.cs && git commit -m "feat(app): PlayerViewModel transport + resume save/offer

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 9: App — end-of-media auto-mark-watched + clear resume + auto-next-episode

Spec §9: on reaching end → auto-mark watched (clears resume) AND auto-advance to next episode by `episode_no` if the setting is on and the series is not a standalone. The view-model exposes a `NextEpisodeRequested` event the host uses to re-`Open` the next episode (keeping the VM host-agnostic and testable).

**Files:**
- Modify: `src/VideoShelf.App/ViewModels/PlayerViewModel.cs`
- Test: `tests/VideoShelf.App.Tests/PlayerEndOfMediaTests.cs` (create)

- [ ] **Step 1: Write the failing test**

Create `tests/VideoShelf.App.Tests/PlayerEndOfMediaTests.cs`:
```csharp
using Shouldly;
using VideoShelf.App.Services;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.Tests;

public class PlayerEndOfMediaTests
{
    private static (LibraryRepository lib, WatchRepository watch, SettingsRepository settings, long seriesId)
        SeedSeries(AppTempDb temp, int episodes)
    {
        var lib = new LibraryRepository(temp.Db);
        var sectionId = lib.UpsertSection(lib.UpsertSource(@"C:\V", "V"), "S");
        var seriesId = lib.UpsertSeries(sectionId, "Base", isStandalone: episodes == 1 ? false : false);
        for (var n = 1; n <= episodes; n++)
            lib.UpsertVideo(seriesId, $@"C:\V\S\e{n}.mp4", n, ".mp4");
        return (lib, new WatchRepository(temp.Db), new SettingsRepository(temp.Db), seriesId);
    }

    private static EpisodeView Ep(LibraryRepository lib, long seriesId, int n)
    {
        foreach (var e in lib.GetEpisodes(seriesId))
            if (e.EpisodeNo == n) return e;
        throw new System.InvalidOperationException("episode not found");
    }

    private static PlayerViewModel NewVm(FakePlaybackEngine engine,
        LibraryRepository lib, WatchRepository watch, SettingsRepository settings)
        => new(engine, lib, watch, settings, new ResumePolicy());

    [Fact]
    public void Ended_marks_watched_and_clears_resume()
    {
        using var temp = new AppTempDb();
        var (lib, watch, settings, seriesId) = SeedSeries(temp, episodes: 1);
        var ep = Ep(lib, seriesId, 1);
        var engine = new FakePlaybackEngine();
        var vm = NewVm(engine, lib, watch, settings);
        vm.Open(ep);
        engine.RaiseLength(100.0);
        engine.RaisePosition(40.0); // some progress saved

        engine.RaiseEnded();

        watch.IsWatched(ep.VideoId).ShouldBeTrue();
        lib.GetResumePosition(ep.VideoId).ShouldBeNull();
    }

    [Fact]
    public void Ended_requests_next_episode_when_auto_advance_on()
    {
        using var temp = new AppTempDb();
        var (lib, watch, settings, seriesId) = SeedSeries(temp, episodes: 2);
        settings.SetAutoAdvanceEpisodes(true);
        var ep1 = Ep(lib, seriesId, 1);
        var engine = new FakePlaybackEngine();
        var vm = NewVm(engine, lib, watch, settings);
        EpisodeView? requested = null;
        vm.NextEpisodeRequested += (_, e) => requested = e;
        vm.Open(ep1);
        engine.RaiseLength(100.0);

        engine.RaiseEnded();

        requested.ShouldNotBeNull();
        requested!.EpisodeNo.ShouldBe(2);
    }

    [Fact]
    public void Ended_does_not_request_next_when_auto_advance_off()
    {
        using var temp = new AppTempDb();
        var (lib, watch, settings, seriesId) = SeedSeries(temp, episodes: 2);
        settings.SetAutoAdvanceEpisodes(false);
        var ep1 = Ep(lib, seriesId, 1);
        var engine = new FakePlaybackEngine();
        var vm = NewVm(engine, lib, watch, settings);
        var fired = false;
        vm.NextEpisodeRequested += (_, _) => fired = true;
        vm.Open(ep1);
        engine.RaiseLength(100.0);

        engine.RaiseEnded();

        fired.ShouldBeFalse();
    }

    [Fact]
    public void Ended_on_last_episode_does_not_request_next()
    {
        using var temp = new AppTempDb();
        var (lib, watch, settings, seriesId) = SeedSeries(temp, episodes: 2);
        settings.SetAutoAdvanceEpisodes(true);
        var ep2 = Ep(lib, seriesId, 2);
        var engine = new FakePlaybackEngine();
        var vm = NewVm(engine, lib, watch, settings);
        var fired = false;
        vm.NextEpisodeRequested += (_, _) => fired = true;
        vm.Open(ep2);
        engine.RaiseLength(100.0);

        engine.RaiseEnded();

        fired.ShouldBeFalse();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && dotnet test tests/VideoShelf.App.Tests/VideoShelf.App.Tests.csproj -c Release --nologo -v q 2>&1 | tail -20`
Expected: FAIL — `PlayerViewModel` has no `NextEpisodeRequested` event and does not subscribe to `Ended`.

- [ ] **Step 3: Write minimal implementation**

In `src/VideoShelf.App/ViewModels/PlayerViewModel.cs`:

(a) Add the event declaration near the top of the class (after the fields):
```csharp
    /// <summary>Raised when end-of-media should advance to the next in-series episode (auto-advance only).</summary>
    public event EventHandler<EpisodeView>? NextEpisodeRequested;
```

(b) In `Open`, subscribe to `Ended` alongside the other event hookups. Replace the event-wiring lines with:
```csharp
        engine.PositionChanged -= OnPositionChanged;
        engine.LengthChanged -= OnLengthChanged;
        engine.Ended -= OnEnded;
        engine.PositionChanged += OnPositionChanged;
        engine.LengthChanged += OnLengthChanged;
        engine.Ended += OnEnded;
```

(c) Add the handler method:
```csharp
    private void OnEnded(object? sender, EventArgs e)
    {
        IsPlaying = false;
        if (_current is not { } cur)
            return;

        // Finishing a video marks it watched, which also clears its resume position.
        watch.SetWatched(cur.VideoId, true);

        if (settings.GetAutoAdvanceEpisodes())
        {
            var next = library.GetNextEpisode(cur.SeriesId, cur.EpisodeNo);
            if (next is not null)
                NextEpisodeRequested?.Invoke(this, next);
        }
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && dotnet test tests/VideoShelf.App.Tests/VideoShelf.App.Tests.csproj -c Release --nologo -v q 2>&1 | tail -8`
Expected: PASS — App test count is now 50, 0 failed.

- [ ] **Step 5: Commit**

```
cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && git add src/VideoShelf.App/ViewModels/PlayerViewModel.cs tests/VideoShelf.App.Tests/PlayerEndOfMediaTests.cs && git commit -m "feat(app): end-of-media auto-watched + clear resume + in-series auto-next

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 10: App — track + chapter view-model mapping (incl. "subtitles off") and volume/fullscreen state

Spec §9: embedded subtitle + audio pickers (live from libVLC, incl. "subtitles off"); chapter navigation (markers + next/prev, render nothing when no chapters); volume; fullscreen toggle. Add observable collections and selection commands to `PlayerViewModel`, mapped from the engine.

**Files:**
- Modify: `src/VideoShelf.App/ViewModels/PlayerViewModel.cs`
- Test: `tests/VideoShelf.App.Tests/PlayerTracksAndChaptersTests.cs` (create)

- [ ] **Step 1: Write the failing test**

Create `tests/VideoShelf.App.Tests/PlayerTracksAndChaptersTests.cs`:
```csharp
using System.Linq;
using Shouldly;
using VideoShelf.App.Services;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.Tests;

public class PlayerTracksAndChaptersTests
{
    private static (PlayerViewModel vm, FakePlaybackEngine engine, EpisodeView ep) Make(AppTempDb temp)
    {
        var lib = new LibraryRepository(temp.Db);
        var seriesId = lib.UpsertSeries(lib.UpsertSection(lib.UpsertSource(@"C:\V", "V"), "S"), "Base", false);
        var path = @"C:\V\S\a.mp4";
        var videoId = lib.UpsertVideo(seriesId, path, 1, ".mp4");
        var ep = new EpisodeView(videoId, seriesId, path, 1, "Base", false, false);
        var engine = new FakePlaybackEngine();
        var vm = new PlayerViewModel(engine, lib, new WatchRepository(temp.Db),
            new SettingsRepository(temp.Db), new ResumePolicy());
        return (vm, engine, ep);
    }

    [Fact]
    public void RefreshTracks_populates_audio_and_subtitle_collections()
    {
        using var temp = new AppTempDb();
        var (vm, engine, ep) = Make(temp);
        engine.AudioTracks.Add(new TrackOption(0, "Japanese"));
        engine.AudioTracks.Add(new TrackOption(1, "English"));
        engine.SubtitleTracks.Add(new TrackOption(TrackOption.SubtitlesOffId, "Off"));
        engine.SubtitleTracks.Add(new TrackOption(3, "English"));
        vm.Open(ep);

        vm.RefreshTracks();

        vm.AudioTracks.Select(t => t.Label).ShouldBe(new[] { "Japanese", "English" });
        vm.SubtitleTracks.First().Id.ShouldBe(TrackOption.SubtitlesOffId);
        vm.HasMultipleAudioTracks.ShouldBeTrue();
    }

    [Fact]
    public void SelectingSubtitleTrack_applies_to_engine()
    {
        using var temp = new AppTempDb();
        var (vm, engine, ep) = Make(temp);
        engine.SubtitleTracks.Add(new TrackOption(TrackOption.SubtitlesOffId, "Off"));
        engine.SubtitleTracks.Add(new TrackOption(3, "English"));
        vm.Open(ep);
        vm.RefreshTracks();

        vm.SelectedSubtitleTrack = vm.SubtitleTracks.First(t => t.Id == 3);

        engine.GetCurrentSubtitleTrack().ShouldBe(3);
    }

    [Fact]
    public void SelectingAudioTrack_applies_to_engine()
    {
        using var temp = new AppTempDb();
        var (vm, engine, ep) = Make(temp);
        engine.AudioTracks.Add(new TrackOption(0, "Japanese"));
        engine.AudioTracks.Add(new TrackOption(1, "English"));
        vm.Open(ep);
        vm.RefreshTracks();

        vm.SelectedAudioTrack = vm.AudioTracks.First(t => t.Id == 1);

        engine.GetCurrentAudioTrack().ShouldBe(1);
    }

    [Fact]
    public void RefreshTracks_populates_chapters_and_HasChapters()
    {
        using var temp = new AppTempDb();
        var (vm, engine, ep) = Make(temp);
        engine.Chapters.Add(new ChapterOption(0, "Intro"));
        engine.Chapters.Add(new ChapterOption(1, "Part 1"));
        vm.Open(ep);

        vm.RefreshTracks();

        vm.Chapters.Count.ShouldBe(2);
        vm.HasChapters.ShouldBeTrue();
    }

    [Fact]
    public void No_chapters_means_HasChapters_false()
    {
        using var temp = new AppTempDb();
        var (vm, engine, ep) = Make(temp);
        vm.Open(ep);

        vm.RefreshTracks();

        vm.HasChapters.ShouldBeFalse();
    }

    [Fact]
    public void NextChapter_and_PreviousChapter_call_engine()
    {
        using var temp = new AppTempDb();
        var (vm, engine, ep) = Make(temp);
        engine.Chapters.Add(new ChapterOption(0, "Intro"));
        vm.Open(ep);
        vm.RefreshTracks();

        vm.NextChapterCommand.Execute(null);
        vm.PreviousChapterCommand.Execute(null);

        engine.NextChapterCalls.ShouldBe(1);
        engine.PreviousChapterCalls.ShouldBe(1);
    }

    [Fact]
    public void Volume_setter_forwards_to_engine()
    {
        using var temp = new AppTempDb();
        var (vm, engine, ep) = Make(temp);
        vm.Open(ep);

        vm.Volume = 40;

        engine.Volume.ShouldBe(40);
    }

    [Fact]
    public void ToggleFullscreen_flips_IsFullscreen()
    {
        using var temp = new AppTempDb();
        var (vm, _, ep) = Make(temp);
        vm.Open(ep);

        vm.ToggleFullscreenCommand.Execute(null);

        vm.IsFullscreen.ShouldBeTrue();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && dotnet test tests/VideoShelf.App.Tests/VideoShelf.App.Tests.csproj -c Release --nologo -v q 2>&1 | tail -20`
Expected: FAIL — `PlayerViewModel` has no `AudioTracks`/`SubtitleTracks`/`Chapters`/`RefreshTracks`/`Volume`/`ToggleFullscreen`.

- [ ] **Step 3: Write minimal implementation**

In `src/VideoShelf.App/ViewModels/PlayerViewModel.cs`:

(a) Add `using System.Collections.ObjectModel;` to the usings.

(b) Add observable collections and properties to the class body (after `ResumePositionSeconds`):
```csharp
    public ObservableCollection<TrackOption> AudioTracks { get; } = [];
    public ObservableCollection<TrackOption> SubtitleTracks { get; } = [];
    public ObservableCollection<ChapterOption> Chapters { get; } = [];

    public bool HasMultipleAudioTracks => AudioTracks.Count > 1;
    public bool HasSubtitleTracks => SubtitleTracks.Count > 1;
    public bool HasChapters => Chapters.Count > 0;

    [ObservableProperty]
    private TrackOption? _selectedAudioTrack;

    [ObservableProperty]
    private TrackOption? _selectedSubtitleTrack;

    [ObservableProperty]
    private bool _isFullscreen;

    public int Volume
    {
        get => engine.Volume;
        set
        {
            if (engine.Volume == value) return;
            engine.Volume = value;
            OnPropertyChanged();
        }
    }

    partial void OnSelectedAudioTrackChanged(TrackOption? value)
    {
        if (value is not null) engine.SetAudioTrack(value.Id);
    }

    partial void OnSelectedSubtitleTrackChanged(TrackOption? value)
    {
        if (value is not null) engine.SetSubtitleTrack(value.Id);
    }

    /// <summary>Re-reads live track/chapter lists from the engine (call when media is ready / on demand).</summary>
    public void RefreshTracks()
    {
        AudioTracks.Clear();
        foreach (var t in engine.GetAudioTracks()) AudioTracks.Add(t);
        SubtitleTracks.Clear();
        foreach (var t in engine.GetSubtitleTracks()) SubtitleTracks.Add(t);
        Chapters.Clear();
        foreach (var c in engine.GetChapters()) Chapters.Add(c);

        OnPropertyChanged(nameof(HasMultipleAudioTracks));
        OnPropertyChanged(nameof(HasSubtitleTracks));
        OnPropertyChanged(nameof(HasChapters));
    }

    [RelayCommand]
    private void NextChapter() => engine.NextChapter();

    [RelayCommand]
    private void PreviousChapter() => engine.PreviousChapter();

    [RelayCommand]
    private void ToggleFullscreen() => IsFullscreen = !IsFullscreen;
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && dotnet test tests/VideoShelf.App.Tests/VideoShelf.App.Tests.csproj -c Release --nologo -v q 2>&1 | tail -8`
Expected: PASS — App test count is now 58, 0 failed.

- [ ] **Step 5: Commit**

```
cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && git add src/VideoShelf.App/ViewModels/PlayerViewModel.cs tests/VideoShelf.App.Tests/PlayerTracksAndChaptersTests.cs && git commit -m "feat(app): track/chapter mapping (incl. subtitles-off), volume, fullscreen

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 11: App — keyboard-command mapping (`PlayerKeyMap`)

Spec §9 shortcuts: space (play/pause), ←/→ (seek), F (fullscreen), Esc (exit fullscreen), `Ctrl+E` (screenshot). Map a key + modifier to a semantic `PlayerCommand` in a pure helper so the binding is unit-testable and the View just forwards `KeyDown`.

**Files:**
- Create: `src/VideoShelf.App/Services/PlayerKeyMap.cs`
- Test: `tests/VideoShelf.App.Tests/PlayerKeyMapTests.cs` (create)

- [ ] **Step 1: Write the failing test**

Create `tests/VideoShelf.App.Tests/PlayerKeyMapTests.cs`:
```csharp
using System.Windows.Input;
using Shouldly;
using VideoShelf.App.Services;

namespace VideoShelf.App.Tests;

public class PlayerKeyMapTests
{
    [Theory]
    [InlineData(Key.Space, ModifierKeys.None, PlayerCommand.TogglePlayPause)]
    [InlineData(Key.Left, ModifierKeys.None, PlayerCommand.SeekBackward)]
    [InlineData(Key.Right, ModifierKeys.None, PlayerCommand.SeekForward)]
    [InlineData(Key.F, ModifierKeys.None, PlayerCommand.ToggleFullscreen)]
    [InlineData(Key.Escape, ModifierKeys.None, PlayerCommand.ExitFullscreen)]
    [InlineData(Key.E, ModifierKeys.Control, PlayerCommand.Screenshot)]
    public void Maps_known_keys(Key key, ModifierKeys mods, PlayerCommand expected)
        => PlayerKeyMap.Resolve(key, mods).ShouldBe(expected);

    [Fact]
    public void Unmapped_key_returns_none()
        => PlayerKeyMap.Resolve(Key.Q, ModifierKeys.None).ShouldBe(PlayerCommand.None);

    [Fact]
    public void E_without_control_is_not_screenshot()
        => PlayerKeyMap.Resolve(Key.E, ModifierKeys.None).ShouldBe(PlayerCommand.None);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && dotnet test tests/VideoShelf.App.Tests/VideoShelf.App.Tests.csproj -c Release --nologo -v q 2>&1 | tail -20`
Expected: FAIL — `PlayerKeyMap`/`PlayerCommand` do not exist.

- [ ] **Step 3: Write minimal implementation**

Create `src/VideoShelf.App/Services/PlayerKeyMap.cs`:
```csharp
using System.Windows.Input;

namespace VideoShelf.App.Services;

public enum PlayerCommand
{
    None,
    TogglePlayPause,
    SeekBackward,
    SeekForward,
    ToggleFullscreen,
    ExitFullscreen,
    Screenshot,
}

/// <summary>Pure keyboard-to-command mapping for the player (spec §9 shortcuts).</summary>
public static class PlayerKeyMap
{
    public static PlayerCommand Resolve(Key key, ModifierKeys modifiers)
    {
        var ctrl = (modifiers & ModifierKeys.Control) == ModifierKeys.Control;

        return (key, ctrl) switch
        {
            (Key.E, true) => PlayerCommand.Screenshot,
            (Key.Space, false) => PlayerCommand.TogglePlayPause,
            (Key.Left, false) => PlayerCommand.SeekBackward,
            (Key.Right, false) => PlayerCommand.SeekForward,
            (Key.F, false) => PlayerCommand.ToggleFullscreen,
            (Key.Escape, false) => PlayerCommand.ExitFullscreen,
            _ => PlayerCommand.None,
        };
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && dotnet test tests/VideoShelf.App.Tests/VideoShelf.App.Tests.csproj -c Release --nologo -v q 2>&1 | tail -8`
Expected: PASS — App test count is now 67, 0 failed.

- [ ] **Step 5: Commit**

```
cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && git add src/VideoShelf.App/Services/PlayerKeyMap.cs tests/VideoShelf.App.Tests/PlayerKeyMapTests.cs && git commit -m "feat(app): PlayerKeyMap keyboard-to-command mapping

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 12: App — screenshot command + seek-preview throttle on `PlayerViewModel`

Spec §9: frame capture (`Ctrl+E` → PNG to a capture folder via libVLC snapshot) and seek-preview thumbnails (hover the seek bar → frame at that position, cached). Add a `ScreenshotCommand` (writes only to a capture folder under `AppPaths`) and a `RequestSeekPreviewAsync` that fail-safely produces a preview PNG path. Snapshot path generation must be deterministic and live ONLY under the capture/preview folders (read-only-on-source principle).

**Files:**
- Modify: `src/VideoShelf.App/Services/AppPaths.cs` (add `CaptureDirectory`, `SeekPreviewDirectory`)
- Modify: `src/VideoShelf.App/ViewModels/PlayerViewModel.cs`
- Test: `tests/VideoShelf.App.Tests/PlayerCaptureTests.cs` (create)
- Test: `tests/VideoShelf.Core.Tests/...` — none (App-side)

- [ ] **Step 1: Write the failing test**

First extend `AppPaths`. Create `tests/VideoShelf.App.Tests/PlayerCaptureTests.cs`:
```csharp
using System.IO;
using Shouldly;
using VideoShelf.App.Services;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.Tests;

public class PlayerCaptureTests
{
    private static (PlayerViewModel vm, FakePlaybackEngine engine) Make(AppTempDb temp, string captureDir)
    {
        var lib = new LibraryRepository(temp.Db);
        var seriesId = lib.UpsertSeries(lib.UpsertSection(lib.UpsertSource(@"C:\V", "V"), "S"), "Base", false);
        var videoId = lib.UpsertVideo(seriesId, @"C:\V\S\a.mp4", 1, ".mp4");
        var ep = new EpisodeView(videoId, seriesId, @"C:\V\S\a.mp4", 1, "Base", false, false);
        var engine = new FakePlaybackEngine();
        var vm = new PlayerViewModel(engine, lib, new WatchRepository(temp.Db),
            new SettingsRepository(temp.Db), new ResumePolicy())
        {
            CaptureDirectory = captureDir,
        };
        vm.Open(ep);
        return (vm, engine);
    }

    [Fact]
    public void AppPaths_exposes_capture_and_preview_dirs_under_root()
    {
        var paths = new AppPaths(@"C:\Root");

        paths.CaptureDirectory.ShouldBe(@"C:\Root\captures");
        paths.SeekPreviewDirectory.ShouldBe(@"C:\Root\seek-preview");
    }

    [Fact]
    public void Screenshot_invokes_engine_snapshot_into_capture_dir()
    {
        using var temp = new AppTempDb();
        using var dir = new TempDir();
        var (vm, engine) = Make(temp, dir.Path);

        vm.ScreenshotCommand.Execute(null);

        engine.SnapshotCount.ShouldBe(1);
        vm.LastScreenshotPath.ShouldNotBeNull();
        Path.GetDirectoryName(vm.LastScreenshotPath!).ShouldBe(dir.Path);
    }

    [Fact]
    public void Screenshot_failure_is_swallowed_and_path_stays_null()
    {
        using var temp = new AppTempDb();
        using var dir = new TempDir();
        var (vm, engine) = Make(temp, dir.Path);
        engine.SnapshotShouldFail = true;

        vm.ScreenshotCommand.Execute(null); // must not throw

        vm.LastScreenshotPath.ShouldBeNull();
    }

    [Fact]
    public async System.Threading.Tasks.Task SeekPreview_returns_null_on_engine_failure()
    {
        using var temp = new AppTempDb();
        using var dir = new TempDir();
        var (vm, engine) = Make(temp, dir.Path);
        vm.SeekPreviewDirectory = dir.Path;
        engine.SnapshotShouldFail = true;

        var path = await vm.RequestSeekPreviewAsync(12.0, System.Threading.CancellationToken.None);

        path.ShouldBeNull();
    }
}
```

Note: `TempDir` lives in `VideoShelf.Core.Tests.TestSupport` and is referenced by App.Tests (the App.Tests csproj references Core.Tests). Add `using VideoShelf.Core.Tests.TestSupport;` to the test file.

- [ ] **Step 2: Run test to verify it fails**

Run: `cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && dotnet test tests/VideoShelf.App.Tests/VideoShelf.App.Tests.csproj -c Release --nologo -v q 2>&1 | tail -20`
Expected: FAIL — `AppPaths.CaptureDirectory`, `PlayerViewModel.ScreenshotCommand`/`CaptureDirectory`/`LastScreenshotPath`/`RequestSeekPreviewAsync` do not exist.

- [ ] **Step 3: Write minimal implementation**

(a) In `src/VideoShelf.App/Services/AppPaths.cs`, add after `ThumbnailDirectory`:
```csharp
    public string CaptureDirectory => Path.Combine(Root, "captures");
    public string SeekPreviewDirectory => Path.Combine(Root, "seek-preview");
```

(b) Add `using VideoShelf.Core.Tests.TestSupport;` at the top of `PlayerCaptureTests.cs` (it pulls in `TempDir`).

(c) In `src/VideoShelf.App/ViewModels/PlayerViewModel.cs`:

Add `using System.IO;`, `using System.Threading;`, and `using System.Threading.Tasks;` to the usings.

Add these members to the class (after the `Volume` property):
```csharp
    /// <summary>Folder screenshots are written to. Set by DI/host; defaults to a temp-safe value for tests.</summary>
    public string CaptureDirectory { get; set; } = System.IO.Path.GetTempPath();

    /// <summary>Folder seek-preview frames are cached in.</summary>
    public string SeekPreviewDirectory { get; set; } = System.IO.Path.GetTempPath();

    [ObservableProperty]
    private string? _lastScreenshotPath;

    [RelayCommand]
    private void Screenshot()
    {
        try
        {
            Directory.CreateDirectory(CaptureDirectory);
            var name = $"capture_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png";
            var target = Path.Combine(CaptureDirectory, name);
            LastScreenshotPath = engine.TrySnapshot(target) ? target : null;
        }
        catch
        {
            LastScreenshotPath = null; // fail-safe: a screenshot must never crash playback
        }
    }

    /// <summary>Produces a seek-preview frame PNG for the given position, or null on failure (fail-safe).</summary>
    public async Task<string?> RequestSeekPreviewAsync(double seconds, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(SeekPreviewDirectory);
            var bucket = (long)seconds; // 1s buckets keep scrubbing cache-friendly
            var target = Path.Combine(SeekPreviewDirectory, $"preview_{bucket}.png");
            if (File.Exists(target) && new FileInfo(target).Length > 0)
                return target;

            var ok = await engine.TryGeneratePreviewFrameAsync(seconds, target, cancellationToken)
                .ConfigureAwait(false);
            return ok && File.Exists(target) && new FileInfo(target).Length > 0 ? target : null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch
        {
            return null; // fail-safe
        }
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && dotnet test tests/VideoShelf.App.Tests/VideoShelf.App.Tests.csproj -c Release --nologo -v q 2>&1 | tail -8`
Expected: PASS — App test count is now 71, 0 failed.

- [ ] **Step 5: Commit**

```
cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && git add src/VideoShelf.App/Services/AppPaths.cs src/VideoShelf.App/ViewModels/PlayerViewModel.cs tests/VideoShelf.App.Tests/PlayerCaptureTests.cs && git commit -m "feat(app): screenshot command + seek-preview frame generation (capture-folder only)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 13: App — missing-file guard before opening playback

Spec §6 missing-file marking: "attempting to play [a missing file] shows a clear 'file not found' message instead of failing." `PlayerViewModel.Open` should detect a missing backing file and surface a status instead of loading the engine.

**Files:**
- Modify: `src/VideoShelf.App/ViewModels/PlayerViewModel.cs`
- Test: `tests/VideoShelf.App.Tests/PlayerMissingFileTests.cs` (create)

- [ ] **Step 1: Write the failing test**

Create `tests/VideoShelf.App.Tests/PlayerMissingFileTests.cs`:
```csharp
using Shouldly;
using VideoShelf.App.Services;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.Tests;

public class PlayerMissingFileTests
{
    private static PlayerViewModel Vm(AppTempDb temp, FakePlaybackEngine engine, out EpisodeView ep, bool missingFlag)
    {
        var lib = new LibraryRepository(temp.Db);
        var seriesId = lib.UpsertSeries(lib.UpsertSection(lib.UpsertSource(@"C:\V", "V"), "S"), "Base", false);
        var path = @"C:\V\S\does-not-exist.mp4";
        var videoId = lib.UpsertVideo(seriesId, path, 1, ".mp4");
        ep = new EpisodeView(videoId, seriesId, path, 1, "Base", Watched: false, Missing: missingFlag);
        return new PlayerViewModel(engine, lib, new WatchRepository(temp.Db),
            new SettingsRepository(temp.Db), new ResumePolicy());
    }

    [Fact]
    public void Open_missing_file_sets_error_and_does_not_load_engine()
    {
        using var temp = new AppTempDb();
        var engine = new FakePlaybackEngine();
        var vm = Vm(temp, engine, out var ep, missingFlag: true);

        vm.Open(ep);

        vm.PlaybackError.ShouldNotBeNullOrEmpty();
        engine.LoadedPath.ShouldBeNull();
        engine.IsPlaying.ShouldBeFalse();
    }

    [Fact]
    public void Open_nonexistent_file_path_sets_error_even_if_flag_clear()
    {
        using var temp = new AppTempDb();
        var engine = new FakePlaybackEngine();
        var vm = Vm(temp, engine, out var ep, missingFlag: false);

        vm.Open(ep); // file truly does not exist on disk

        vm.PlaybackError.ShouldNotBeNullOrEmpty();
        engine.LoadedPath.ShouldBeNull();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && dotnet test tests/VideoShelf.App.Tests/VideoShelf.App.Tests.csproj -c Release --nologo -v q 2>&1 | tail -20`
Expected: FAIL — `PlayerViewModel` has no `PlaybackError` and `Open` still loads the engine.

- [ ] **Step 3: Write minimal implementation**

In `src/VideoShelf.App/ViewModels/PlayerViewModel.cs`:

(a) Add an observable property (near `Title`):
```csharp
    [ObservableProperty]
    private string? _playbackError;
```

(b) At the very start of `Open(EpisodeView episode)` (before setting `_current`), add the guard:
```csharp
        PlaybackError = null;
        if (episode.Missing || !System.IO.File.Exists(episode.FilePath))
        {
            PlaybackError = $"File not found:\n{episode.FilePath}";
            Title = episode.Title;
            return;
        }
```

Also subscribe to the engine's `EncounteredError` in the event-wiring block of `Open` so runtime decode failures surface too. Update the wiring block to:
```csharp
        engine.PositionChanged -= OnPositionChanged;
        engine.LengthChanged -= OnLengthChanged;
        engine.Ended -= OnEnded;
        engine.EncounteredError -= OnEngineError;
        engine.PositionChanged += OnPositionChanged;
        engine.LengthChanged += OnLengthChanged;
        engine.Ended += OnEnded;
        engine.EncounteredError += OnEngineError;
```

(c) Add the handler:
```csharp
    private void OnEngineError(object? sender, EventArgs e)
    {
        IsPlaying = false;
        PlaybackError = _current is { } cur
            ? $"This video could not be played:\n{cur.FilePath}"
            : "This video could not be played.";
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && dotnet test tests/VideoShelf.App.Tests/VideoShelf.App.Tests.csproj -c Release --nologo -v q 2>&1 | tail -8`
Expected: PASS — App test count is now 73, 0 failed.

- [ ] **Step 5: Commit**

```
cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && git add src/VideoShelf.App/ViewModels/PlayerViewModel.cs tests/VideoShelf.App.Tests/PlayerMissingFileTests.cs && git commit -m "feat(app): missing-file guard + engine-error surface in PlayerViewModel

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 14: App — launch playback from the library (`EpisodeViewModel.Play` + host event)

Spec §6/§9: the user launches playback from an episode. Wire a `PlayCommand` on `EpisodeViewModel` that raises a `PlayRequested` event carrying the `EpisodeView`, which the shell (`MainViewModel`) routes to the player. This keeps the launch path testable without a window.

**Files:**
- Modify: `src/VideoShelf.App/ViewModels/EpisodeViewModel.cs`
- Modify: `src/VideoShelf.App/ViewModels/SeriesViewModel.cs` (re-raise child `PlayRequested`)
- Modify: `src/VideoShelf.App/ViewModels/SectionViewModel.cs` (re-raise series `PlayRequested`)
- Test: `tests/VideoShelf.App.Tests/EpisodePlayRequestTests.cs` (create)

- [ ] **Step 1: Write the failing test**

Create `tests/VideoShelf.App.Tests/EpisodePlayRequestTests.cs`:
```csharp
using Shouldly;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.Tests;

public class EpisodePlayRequestTests
{
    [Fact]
    public void PlayCommand_raises_PlayRequested_with_model()
    {
        using var temp = new AppTempDb();
        var lib = new LibraryRepository(temp.Db);
        var seriesId = lib.UpsertSeries(lib.UpsertSection(lib.UpsertSource(@"C:\V", "V"), "S"), "Base", false);
        var videoId = lib.UpsertVideo(seriesId, @"C:\V\S\a.mp4", 1, ".mp4");
        var view = new EpisodeView(videoId, seriesId, @"C:\V\S\a.mp4", 1, "Base", false, false);
        var vm = new EpisodeViewModel(view, new WatchRepository(temp.Db));

        EpisodeView? requested = null;
        vm.PlayRequested += (_, e) => requested = e;
        vm.PlayCommand.Execute(null);

        requested.ShouldNotBeNull();
        requested!.VideoId.ShouldBe(videoId);
        requested.FilePath.ShouldBe(@"C:\V\S\a.mp4");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && dotnet test tests/VideoShelf.App.Tests/VideoShelf.App.Tests.csproj -c Release --nologo -v q 2>&1 | tail -20`
Expected: FAIL — `EpisodeViewModel` has no `PlayCommand`/`PlayRequested`.

- [ ] **Step 3: Write minimal implementation**

(a) In `src/VideoShelf.App/ViewModels/EpisodeViewModel.cs`, add the event + command (the `model` parameter is the primary-ctor `EpisodeView`):
```csharp
    /// <summary>Raised when the user asks to play this episode; the shell routes it to the player.</summary>
    public event System.EventHandler<EpisodeView>? PlayRequested;

    [RelayCommand]
    private void Play() => PlayRequested?.Invoke(this, model);
```
(`model` is already in scope from the primary constructor; `EpisodeView` is already imported via `VideoShelf.Core.Models`.)

(b) In `src/VideoShelf.App/ViewModels/SeriesViewModel.cs`, re-raise child requests. Add an event and subscribe when building episode VMs. Add the event field after `UnwatchedChanged`:
```csharp
    public event System.EventHandler<EpisodeView>? PlayRequested;
```
In `LoadEpisodesAsync`, inside the `foreach (var row in rows)` loop, after `ep.WatchedChanged += ...`, add:
```csharp
            ep.PlayRequested += (_, e) => PlayRequested?.Invoke(this, e);
```

(c) In `src/VideoShelf.App/ViewModels/SectionViewModel.cs`, re-raise series requests. Add an event after `OnUnwatchedCountChanged`:
```csharp
    public event System.EventHandler<EpisodeView>? PlayRequested;
```
In `LoadSeriesAsync`, inside the `foreach (var s in summaries)` loop, after `seriesVm.UnwatchedChanged += ...`, add:
```csharp
            seriesVm.PlayRequested += (_, e) => PlayRequested?.Invoke(this, e);
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && dotnet test tests/VideoShelf.App.Tests/VideoShelf.App.Tests.csproj -c Release --nologo -v q 2>&1 | tail -8`
Expected: PASS — App test count is now 74, 0 failed.

- [ ] **Step 5: Commit**

```
cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && git add src/VideoShelf.App/ViewModels/EpisodeViewModel.cs src/VideoShelf.App/ViewModels/SeriesViewModel.cs src/VideoShelf.App/ViewModels/SectionViewModel.cs tests/VideoShelf.App.Tests/EpisodePlayRequestTests.cs && git commit -m "feat(app): launch playback from an episode via PlayRequested event chain

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 15: App — `MainViewModel` hosts the player + routes play/auto-next/PiP

Spec §9: the shell owns the active player, routes episode `PlayRequested` into `PlayerViewModel.Open`, re-opens on `NextEpisodeRequested` (auto-next), and exposes a PiP toggle flag the View binds to. Keep it testable: `MainViewModel` takes `PlayerViewModel` via DI; subscriptions are wired in code; tests use a `FakePlaybackEngine`-backed `PlayerViewModel`.

**Files:**
- Modify: `src/VideoShelf.App/ViewModels/MainViewModel.cs`
- Modify: `src/VideoShelf.App/ViewModels/LibraryViewModel.cs` (surface a `PlayRequested` event from sections)
- Test: `tests/VideoShelf.App.Tests/MainViewModelPlaybackTests.cs` (create)

- [ ] **Step 1: Write the failing test**

Create `tests/VideoShelf.App.Tests/MainViewModelPlaybackTests.cs`:
```csharp
using Shouldly;
using VideoShelf.App.Services;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.Tests;

public class MainViewModelPlaybackTests
{
    private sealed class NullThumbs : IThumbnailService
    {
        public System.Threading.Tasks.Task<string?> GetThumbnailPathAsync(string videoPath, System.Threading.CancellationToken ct)
            => System.Threading.Tasks.Task.FromResult<string?>(null);
    }

    private sealed class NullScan : IScanCoordinator
    {
        public System.Threading.Tasks.Task ScanAllAsync(System.Threading.CancellationToken ct)
            => System.Threading.Tasks.Task.CompletedTask;
    }

    private static MainViewModel Make(AppTempDb temp, FakePlaybackEngine engine, out long videoId)
    {
        var lib = new LibraryRepository(temp.Db);
        var watch = new WatchRepository(temp.Db);
        var settings = new SettingsRepository(temp.Db);
        var seriesId = lib.UpsertSeries(lib.UpsertSection(lib.UpsertSource(@"C:\V", "V"), "S"), "Base", false);
        videoId = lib.UpsertVideo(seriesId, @"C:\V\S\a.mp4", 1, ".mp4");
        var thumbs = new NullThumbs();
        var library = new LibraryViewModel(lib, watch, thumbs);
        var sources = new SourcesViewModel(lib);
        var player = new PlayerViewModel(engine, lib, watch, settings, new ResumePolicy());
        return new MainViewModel(sources, library, new NullScan(), player);
    }

    [Fact]
    public void Playing_an_episode_opens_the_player_and_shows_player_pane()
    {
        using var temp = new AppTempDb();
        var engine = new FakePlaybackEngine();
        var vm = Make(temp, engine, out var videoId);
        var ep = new EpisodeView(videoId, 1, @"C:\V\S\a.mp4", 1, "Base", false, false);

        vm.PlayEpisode(ep);

        vm.IsPlayerVisible.ShouldBeTrue();
        // missing path → PlaybackError set, engine not loaded; the routing itself is what we assert:
        vm.Player.Title.ShouldBe("Base");
    }

    [Fact]
    public void TogglePiP_flips_IsPictureInPicture()
    {
        using var temp = new AppTempDb();
        var engine = new FakePlaybackEngine();
        var vm = Make(temp, engine, out _);

        vm.TogglePictureInPictureCommand.Execute(null);

        vm.IsPictureInPicture.ShouldBeTrue();
    }

    [Fact]
    public void NextEpisodeRequested_from_player_reopens_via_PlayEpisode()
    {
        using var temp = new AppTempDb();
        var engine = new FakePlaybackEngine();
        var vm = Make(temp, engine, out _);
        var ep = new EpisodeView(1, 1, @"C:\V\S\a.mp4", 1, "Base", false, false);
        var next = new EpisodeView(2, 1, @"C:\V\S\b.mp4", 2, "Base 2", false, false);
        vm.PlayEpisode(ep);

        vm.Player.RaiseNextEpisodeForTest(next);

        vm.Player.Title.ShouldBe("Base 2");
    }
}
```

Add a tiny test hook to `PlayerViewModel` (Step 3c) so the test can fire `NextEpisodeRequested`.

- [ ] **Step 2: Run test to verify it fails**

Run: `cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && dotnet test tests/VideoShelf.App.Tests/VideoShelf.App.Tests.csproj -c Release --nologo -v q 2>&1 | tail -20`
Expected: FAIL — `MainViewModel` ctor has no `PlayerViewModel` param and lacks `PlayEpisode`/`IsPlayerVisible`/`IsPictureInPicture`/`TogglePictureInPictureCommand`; `PlayerViewModel` lacks `RaiseNextEpisodeForTest`.

- [ ] **Step 3: Write minimal implementation**

(a) In `src/VideoShelf.App/ViewModels/LibraryViewModel.cs`, surface a `PlayRequested` event and wire it when building section VMs. Add the event field (after the `ObservableCollection` declarations):
```csharp
    public event System.EventHandler<VideoShelf.Core.Models.EpisodeView>? PlayRequested;
```
In `LoadSectionsAsync`, inside the `foreach (var s in summaries)` loop, replace the single `Sections.Add(...)` line with:
```csharp
        {
            var sectionVm = new SectionViewModel(s, library, watch, thumbnails);
            sectionVm.PlayRequested += (_, e) => PlayRequested?.Invoke(this, e);
            Sections.Add(sectionVm);
        }
```

(b) In `src/VideoShelf.App/ViewModels/MainViewModel.cs`, rewrite to host the player. Full file:
```csharp
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoShelf.App.Services;
using VideoShelf.Core.Models;

namespace VideoShelf.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly SourcesViewModel _sources;
    private readonly LibraryViewModel _library;
    private readonly IScanCoordinator _scanCoordinator;
    private readonly PlayerViewModel _player;

    public MainViewModel(
        SourcesViewModel sources,
        LibraryViewModel library,
        IScanCoordinator scanCoordinator,
        PlayerViewModel player)
    {
        _sources = sources;
        _library = library;
        _scanCoordinator = scanCoordinator;
        _player = player;

        _library.PlayRequested += (_, ep) => PlayEpisode(ep);
        _player.NextEpisodeRequested += (_, ep) => PlayEpisode(ep);
    }

    public string Title => "VideoShelf";

    public SourcesViewModel Sources => _sources;
    public LibraryViewModel Library => _library;
    public PlayerViewModel Player => _player;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private bool _isPlayerVisible;

    [ObservableProperty]
    private bool _isPictureInPicture;

    /// <summary>Routes a play request into the player and shows the player pane.</summary>
    public void PlayEpisode(EpisodeView episode)
    {
        IsPlayerVisible = true;
        _player.Open(episode);
    }

    [RelayCommand]
    private void TogglePictureInPicture() => IsPictureInPicture = !IsPictureInPicture;

    [RelayCommand]
    private void ClosePlayer()
    {
        _player.FlushResume();
        _player.Engine.Stop();
        IsPlayerVisible = false;
        IsPictureInPicture = false;
    }

    /// <summary>Loads sources + library once at startup.</summary>
    public async Task InitializeAsync()
    {
        Sources.Load();
        await Library.LoadSectionsAsync();
    }

    [RelayCommand]
    private async Task ScanAndReload()
    {
        IsScanning = true;
        try
        {
            await _scanCoordinator.ScanAllAsync(CancellationToken.None);
            Sources.Load();
            await Library.LoadSectionsAsync();
        }
        finally
        {
            IsScanning = false;
        }
    }
}
```

(c) In `src/VideoShelf.App/ViewModels/PlayerViewModel.cs`, add a test hook just under the `NextEpisodeRequested` event:
```csharp
    /// <summary>Test hook: simulates the engine reaching the end and requesting the given next episode.</summary>
    public void RaiseNextEpisodeForTest(EpisodeView next) => NextEpisodeRequested?.Invoke(this, next);
```
And make `RaiseNextEpisodeForTest` also `Open` is NOT needed — but the test asserts `Player.Title == "Base 2"`, so the host's subscription calls `PlayEpisode(next)` → `Open(next)` which sets `Title`. (The `Open` guard will set `PlaybackError` because the file doesn't exist, but `Title` is still assigned — which is what the test checks.)

- [ ] **Step 4: Run test to verify it passes**

Run: `cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && dotnet test tests/VideoShelf.App.Tests/VideoShelf.App.Tests.csproj -c Release --nologo -v q 2>&1 | tail -8`
Expected: PASS — App test count is now 77, 0 failed. (Existing `MainViewModelTests` may need the new ctor arg — see Step 4b note.)

- [ ] **Step 4b: Fix any existing MainViewModel test construction**

If `tests/VideoShelf.App.Tests/MainViewModelTests.cs` constructs `MainViewModel` directly, it now needs a fourth `PlayerViewModel` argument. Inspect it:
Run: `cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && grep -n "new MainViewModel" tests/VideoShelf.App.Tests/MainViewModelTests.cs`
If it constructs the VM, update each call to pass a `PlayerViewModel` built from a `FakePlaybackEngine` (pattern as in `MainViewModelPlaybackTests.Make`). Re-run the App test gate until green. If it only resolves via DI, no change is needed here (the DI wiring is Task 16).

- [ ] **Step 5: Commit**

```
cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && git add src/VideoShelf.App/ViewModels/MainViewModel.cs src/VideoShelf.App/ViewModels/LibraryViewModel.cs src/VideoShelf.App/ViewModels/PlayerViewModel.cs tests/VideoShelf.App.Tests/MainViewModelPlaybackTests.cs tests/VideoShelf.App.Tests/MainViewModelTests.cs && git commit -m "feat(app): MainViewModel hosts player, routes play + auto-next + PiP toggle

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 16: App — DI wiring for the playback engine, player VM, settings, and capture folders

Register the new services so `MainViewModel` resolves with a real engine in production. The concrete `LibVlcPlaybackEngine` does not exist yet (Task 17); register a temporary no-op until then OR sequence Task 17 before this. To keep the build green at every commit, this task registers `SettingsRepository` + `PlayerViewModel` and points `PlayerViewModel`'s capture/preview dirs at `AppPaths`, and registers `IPlaybackEngine` to the concrete type added in Task 17. **Therefore implement Task 17 BEFORE this task** (the plan orders 16 after 17 conceptually; if executing strictly in number order, do 17's engine first). See Step 3 note.

> Execution note: do **Task 17 first**, then this task. They are split only so the engine has its own verification gate. If you prefer, merge them — but commit the engine before referencing it here.

**Files:**
- Modify: `src/VideoShelf.App/Services/ServiceCollectionExtensions.cs`
- Test: `tests/VideoShelf.App.Tests/HostBuildsTests.cs` (add a player-resolves assertion)

- [ ] **Step 1: Write the failing test**

Append to `tests/VideoShelf.App.Tests/HostBuildsTests.cs` inside the class:
```csharp
    [Fact]
    public void AddVideoShelf_resolves_player_viewmodel()
    {
        var provider = new ServiceCollection().AddVideoShelf().BuildServiceProvider();

        var player = provider.GetRequiredService<VideoShelf.App.ViewModels.PlayerViewModel>();

        player.ShouldNotBeNull();
    }

    [Fact]
    public void AddVideoShelf_resolves_settings_repository()
    {
        var provider = new ServiceCollection().AddVideoShelf().BuildServiceProvider();

        provider.GetRequiredService<VideoShelf.Core.Storage.SettingsRepository>().ShouldNotBeNull();
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && dotnet test tests/VideoShelf.App.Tests/VideoShelf.App.Tests.csproj -c Release --nologo -v q 2>&1 | tail -20`
Expected: FAIL — `PlayerViewModel`/`SettingsRepository` are not registered.

- [ ] **Step 3: Write minimal implementation**

In `src/VideoShelf.App/Services/ServiceCollectionExtensions.cs`, add registrations. Add these inside `AddVideoShelf`, after the `WatchRepository` registration:
```csharp
        services.AddSingleton<SettingsRepository>();
```
After the thumbnail registrations, add the engine + resume policy + player:
```csharp
        services.AddSingleton<ResumePolicy>();
        services.AddSingleton<IPlaybackEngine, LibVlcPlaybackEngine>();
        services.AddSingleton<PlayerViewModel>(sp =>
        {
            var paths = sp.GetRequiredService<AppPaths>();
            var vm = new PlayerViewModel(
                sp.GetRequiredService<IPlaybackEngine>(),
                sp.GetRequiredService<LibraryRepository>(),
                sp.GetRequiredService<WatchRepository>(),
                sp.GetRequiredService<SettingsRepository>(),
                sp.GetRequiredService<ResumePolicy>())
            {
                CaptureDirectory = paths.CaptureDirectory,
                SeekPreviewDirectory = paths.SeekPreviewDirectory,
            };
            return vm;
        });
```
Ensure `using VideoShelf.Core.Storage;` (already present) covers `SettingsRepository`. `LibVlcPlaybackEngine`, `IPlaybackEngine`, `ResumePolicy`, `PlayerViewModel` are in `VideoShelf.App.Services`/`VideoShelf.App.ViewModels` (add `using VideoShelf.App.ViewModels;` — already imported).

> NOTE: This references `LibVlcPlaybackEngine` from Task 17. Implement Task 17 first so this compiles.

- [ ] **Step 4: Run test to verify it passes**

Run: `cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && dotnet test VideoShelf.slnx -c Release --nologo -v q 2>&1 | tail -8`
Expected: PASS — full suite green.

- [ ] **Step 5: Commit**

```
cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && git add src/VideoShelf.App/Services/ServiceCollectionExtensions.cs tests/VideoShelf.App.Tests/HostBuildsTests.cs && git commit -m "feat(app): DI wiring for player VM, engine, settings, capture folders

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 17: App — `LibVlcPlaybackEngine` (thin libVLC concrete) + `LibVLCSharp.WPF` package

**Integration component — no unit tests.** Implements `IPlaybackEngine` over `LibVLCSharp` `MediaPlayer`, exposing the libVLC `MediaPlayer` for the `VideoView` to bind. Verification gate: Release build succeeds. The Phase 6 harness later screenshots real playback.

> **Execution order:** implement this task BEFORE Task 16 (Task 16's DI references this type). It is numbered 17 only so its build-only gate reads clearly after the testable VM work.

**Files:**
- Modify: `src/VideoShelf.App/VideoShelf.App.csproj` (add `LibVLCSharp.WPF` 3.9.7.1)
- Create: `src/VideoShelf.App/Services/LibVlcPlaybackEngine.cs`

- [ ] **Step 1: Add the WPF libVLC package**

In `src/VideoShelf.App/VideoShelf.App.csproj`, add to the `PackageReference` ItemGroup (alongside WPF-UI):
```xml
    <PackageReference Include="LibVLCSharp.WPF" Version="3.9.7.1" />
```
(`LibVLCSharp` + `VideoLAN.LibVLC.Windows` are transitively available via the Core project reference; `LibVLCSharp.WPF` adds the `VideoView` control.)

- [ ] **Step 2: Implement the concrete engine**

Create `src/VideoShelf.App/Services/LibVlcPlaybackEngine.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LibVLCSharp.Shared;

namespace VideoShelf.App.Services;

/// <summary>
/// Thin libVLC-backed IPlaybackEngine. Owns a LibVLC + MediaPlayer; the View binds the MediaPlayer
/// to a LibVLCSharp.WPF VideoView. Fail-safe by contract: errors raise EncounteredError, never throw.
/// Not unit-tested (integration); covered by the Phase 6 harness with generated clips.
/// </summary>
public sealed class LibVlcPlaybackEngine : IPlaybackEngine
{
    private readonly LibVLC _libVlc;
    private readonly MediaPlayer _player;

    /// <summary>The underlying libVLC player, for the VideoView to host. App-internal use only.</summary>
    public MediaPlayer MediaPlayer => _player;

    public LibVlcPlaybackEngine()
    {
        LibVLCSharp.Shared.Core.Initialize();
        _libVlc = new LibVLC("--no-video-title-show", "--quiet");
        _player = new MediaPlayer(_libVlc);

        _player.TimeChanged += (_, e) => PositionChanged?.Invoke(this, e.Time / 1000.0);
        _player.LengthChanged += (_, e) => LengthChanged?.Invoke(this, e.Length / 1000.0);
        _player.EndReached += (_, _) => Ended?.Invoke(this, EventArgs.Empty);
        _player.EncounteredError += (_, _) => EncounteredError?.Invoke(this, EventArgs.Empty);
    }

    public void Load(string filePath)
    {
        try
        {
            var media = new Media(_libVlc, new Uri(filePath));
            _player.Media = media;
            media.Dispose(); // MediaPlayer retains its own reference
        }
        catch
        {
            EncounteredError?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Play() { try { _player.Play(); } catch { EncounteredError?.Invoke(this, EventArgs.Empty); } }
    public void Pause() { try { _player.SetPause(true); } catch { } }
    public void Stop() { try { _player.Stop(); } catch { } }
    public bool IsPlaying => _player.IsPlaying;

    public double Position => _player.Time / 1000.0;
    public double Length => _player.Length / 1000.0;
    public void SeekTo(double seconds) { try { _player.Time = (long)(seconds * 1000); } catch { } }

    public int Volume
    {
        get => _player.Volume;
        set { try { _player.Volume = Math.Clamp(value, 0, 100); } catch { } }
    }

    public IReadOnlyList<TrackOption> GetAudioTracks()
    {
        var list = new List<TrackOption>();
        try
        {
            foreach (var d in _player.AudioTrackDescription)
                if (d.Id >= 0) // -1 is the libVLC "disable audio" pseudo-track; we don't surface it
                    list.Add(new TrackOption(d.Id, d.Name ?? $"Audio {d.Id}"));
        }
        catch { }
        return list;
    }

    public int GetCurrentAudioTrack() { try { return _player.AudioTrack; } catch { return -1; } }
    public void SetAudioTrack(int id) { try { _player.SetAudioTrack(id); } catch { } }

    public IReadOnlyList<TrackOption> GetSubtitleTracks()
    {
        var list = new List<TrackOption>();
        try
        {
            // Always offer "subtitles off" first.
            list.Add(new TrackOption(TrackOption.SubtitlesOffId, "Off"));
            foreach (var d in _player.SpuDescription)
                if (d.Id >= 0)
                    list.Add(new TrackOption(d.Id, d.Name ?? $"Subtitle {d.Id}"));
        }
        catch { }
        return list;
    }

    public int GetCurrentSubtitleTrack() { try { return _player.Spu; } catch { return TrackOption.SubtitlesOffId; } }
    public void SetSubtitleTrack(int id) { try { _player.SetSpu(id); } catch { } }

    public IReadOnlyList<ChapterOption> GetChapters()
    {
        var list = new List<ChapterOption>();
        try
        {
            var chapters = _player.FullChapterDescriptions();
            if (chapters is not null)
                for (var i = 0; i < chapters.Length; i++)
                    list.Add(new ChapterOption(i, chapters[i].Name ?? $"Chapter {i + 1}"));
        }
        catch { }
        return list;
    }

    public void NextChapter() { try { _player.NextChapter(); } catch { } }
    public void PreviousChapter() { try { _player.PreviousChapter(); } catch { } }

    public bool TrySnapshot(string outputPngPath)
    {
        try
        {
            return _player.TakeSnapshot(0, outputPngPath, 0, 0)
                && File.Exists(outputPngPath) && new FileInfo(outputPngPath).Length > 0;
        }
        catch { return false; }
    }

    public async Task<bool> TryGeneratePreviewFrameAsync(double seconds, string outputPngPath, CancellationToken cancellationToken)
    {
        // Seek-preview uses the live player's snapshot at the hovered time. A dedicated off-screen
        // decode is a Phase 6 refinement; here we snapshot the current frame fail-safely.
        try
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            return TrySnapshot(outputPngPath);
        }
        catch { return false; }
    }

    public event EventHandler<double>? PositionChanged;
    public event EventHandler<double>? LengthChanged;
    public event EventHandler? Ended;
    public event EventHandler? EncounteredError;

    public void Dispose()
    {
        try { _player.Dispose(); } catch { }
        try { _libVlc.Dispose(); } catch { }
    }
}
```

> **API caution (resolve at implement time):** `FullChapterDescriptions()`, `SpuDescription`, `AudioTrackDescription`, `SetSpu`, `SetAudioTrack`, `Spu`, `AudioTrack`, `NextChapter`/`PreviousChapter` are the LibVLCSharp 3.x `MediaPlayer` members. If a member name differs in 3.9.7.1, fix to the actual API (verify via the package's MediaPlayer surface) — the `IPlaybackEngine` contract and all unit-tested logic stay unchanged. This is the one place allowed to adapt to the real libVLC surface.

- [ ] **Step 3: Verify (build only — integration component, no unit test)**

Run: `cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && dotnet build VideoShelf.slnx -c Release --nologo 2>&1 | tail -15`
Expected: Build succeeded, 0 errors. (If chapter/track member names mismatch, adjust per the API-caution note and rebuild.)

> **Phase 6 harness must later screenshot:** real playback of a generated clip (video renders in the VideoView), the overlay control bar, subtitle/audio pickers populated from a multi-track clip, chapter markers + next/prev on a chaptered clip, a saved screenshot PNG appearing in the capture folder, and a seek-preview thumbnail on hover.

- [ ] **Step 4: Commit**

```
cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && git add src/VideoShelf.App/VideoShelf.App.csproj src/VideoShelf.App/Services/LibVlcPlaybackEngine.cs && git commit -m "feat(app): LibVlcPlaybackEngine (thin libVLC concrete) + LibVLCSharp.WPF package

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 18: App — `PlayerView` UserControl hosting `VideoView` + overlay controls (XAML)

**Integration component — no unit tests.** The embedded player View: a `LibVLCSharp.WPF.VideoView` bound to the engine's `MediaPlayer`, with an additive overlay control bar (play/pause, seek slider with current/total time, volume, fullscreen toggle, audio/subtitle pickers, chapter prev/next, screenshot), a resume-offer banner, and a missing-file/error banner. Keyboard handling forwards `KeyDown` through `PlayerKeyMap`. **Theming rule:** additive overlay only — never re-template WPF-UI controls.

**Files:**
- Create: `src/VideoShelf.App/Views/PlayerView.xaml`
- Create: `src/VideoShelf.App/Views/PlayerView.xaml.cs`
- Modify: `src/VideoShelf.App/Views/MainWindow.xaml` (host the player pane bound to `Player`, shown when `IsPlayerVisible` and not PiP)

- [ ] **Step 1: Create the PlayerView XAML**

Create `src/VideoShelf.App/Views/PlayerView.xaml`:
```xml
<UserControl x:Class="VideoShelf.App.Views.PlayerView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
             xmlns:vlc="clr-namespace:LibVLCSharp.WPF;assembly=LibVLCSharp.WPF"
             xmlns:conv="clr-namespace:VideoShelf.App.Converters"
             Focusable="True"
             Background="Black">
    <UserControl.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="/VideoShelf.App;component/Resources/DesignTokens.xaml" />
            </ResourceDictionary.MergedDictionaries>
            <conv:BoolToVisibility x:Key="BoolToVisibility" />
        </ResourceDictionary>
    </UserControl.Resources>

    <Grid>
        <!-- The libVLC render surface. Its MediaPlayer is assigned in code-behind. -->
        <vlc:VideoView x:Name="VideoSurface" />

        <!-- Error / missing-file banner (additive) -->
        <Border VerticalAlignment="Center" HorizontalAlignment="Center"
                Padding="20" CornerRadius="8" Background="#CC202020"
                Visibility="{Binding Player.PlaybackError, Converter={StaticResource BoolToVisibility}}">
            <TextBlock Text="{Binding Player.PlaybackError}" Foreground="White"
                       TextAlignment="Center" TextWrapping="Wrap" MaxWidth="480" />
        </Border>

        <!-- Resume-offer banner (additive) -->
        <Border VerticalAlignment="Top" HorizontalAlignment="Center" Margin="0,16,0,0"
                Padding="14,8" CornerRadius="6" Background="#CC202020"
                Visibility="{Binding Player.CanResume, Converter={StaticResource BoolToVisibility}}">
            <StackPanel Orientation="Horizontal">
                <TextBlock Text="Resume where you left off?" Foreground="White"
                           VerticalAlignment="Center" Margin="0,0,12,0" />
                <ui:Button Content="Resume" Command="{Binding Player.ResumeCommand}" />
            </StackPanel>
        </Border>

        <!-- Overlay control bar (additive) -->
        <Border VerticalAlignment="Bottom" Background="#B0101010" Padding="12,8">
            <StackPanel>
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="Auto" />
                        <ColumnDefinition Width="*" />
                        <ColumnDefinition Width="Auto" />
                    </Grid.ColumnDefinitions>
                    <TextBlock Grid.Column="0" Foreground="White" VerticalAlignment="Center"
                               Text="{Binding Player.PositionSeconds, StringFormat={}{0:0}s}" Margin="0,0,8,0" />
                    <Slider Grid.Column="1" x:Name="SeekBar" VerticalAlignment="Center"
                            Minimum="0" Maximum="{Binding Player.LengthSeconds}"
                            Value="{Binding Player.PositionSeconds, Mode=OneWay}" />
                    <TextBlock Grid.Column="2" Foreground="White" VerticalAlignment="Center"
                               Text="{Binding Player.LengthSeconds, StringFormat={}{0:0}s}" Margin="8,0,0,0" />
                </Grid>
                <StackPanel Orientation="Horizontal" Margin="0,8,0,0">
                    <ui:Button Content="Play/Pause" Command="{Binding Player.TogglePlayPauseCommand}" />
                    <ui:Button Content="◀ Chapter" Margin="8,0,0,0"
                               Command="{Binding Player.PreviousChapterCommand}"
                               Visibility="{Binding Player.HasChapters, Converter={StaticResource BoolToVisibility}}" />
                    <ui:Button Content="Chapter ▶" Margin="4,0,0,0"
                               Command="{Binding Player.NextChapterCommand}"
                               Visibility="{Binding Player.HasChapters, Converter={StaticResource BoolToVisibility}}" />
                    <ComboBox Margin="8,0,0,0" Width="140"
                              ItemsSource="{Binding Player.AudioTracks}"
                              SelectedItem="{Binding Player.SelectedAudioTrack}"
                              DisplayMemberPath="Label"
                              Visibility="{Binding Player.HasMultipleAudioTracks, Converter={StaticResource BoolToVisibility}}" />
                    <ComboBox Margin="8,0,0,0" Width="140"
                              ItemsSource="{Binding Player.SubtitleTracks}"
                              SelectedItem="{Binding Player.SelectedSubtitleTrack}"
                              DisplayMemberPath="Label"
                              Visibility="{Binding Player.HasSubtitleTracks, Converter={StaticResource BoolToVisibility}}" />
                    <Slider Margin="12,0,0,0" Width="100" VerticalAlignment="Center"
                            Minimum="0" Maximum="100" Value="{Binding Player.Volume}" />
                    <ui:Button Content="Screenshot" Margin="8,0,0,0" Command="{Binding Player.ScreenshotCommand}" />
                    <ui:Button Content="Fullscreen" Margin="8,0,0,0" Command="{Binding Player.ToggleFullscreenCommand}" />
                    <ui:Button Content="Mini-player" Margin="8,0,0,0" Command="{Binding TogglePictureInPictureCommand}" />
                    <ui:Button Content="Close" Margin="8,0,0,0" Command="{Binding ClosePlayerCommand}" />
                </StackPanel>
            </StackPanel>
        </Border>
    </Grid>
</UserControl>
```

> If the existing `BoolToVisibility` converter does not also collapse on null/empty strings, the error/resume banners must bind to a bool. To stay safe, in Step 2 the code-behind seeks on slider drag; if the converter is strictly bool-only, change the two string-bound `Visibility` bindings to dedicated bool properties — but `MissingToOpacity`/`BoolToVisibility` from Phase 2 already exist; verify `BoolToVisibility` handling of non-bool. If unsupported, add a `HasError => !string.IsNullOrEmpty(PlaybackError)` bool to `PlayerViewModel` and bind to it. (Cheap additive change; do it if the build/binding warns.)

- [ ] **Step 2: Create the code-behind (engine binding + keyboard + slider seek)**

Create `src/VideoShelf.App/Views/PlayerView.xaml.cs`:
```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VideoShelf.App.Services;
using VideoShelf.App.ViewModels;

namespace VideoShelf.App.Views;

public partial class PlayerView : UserControl
{
    public PlayerView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        KeyDown += OnKeyDown;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Bind the libVLC MediaPlayer to the VideoView once the visual tree is ready.
        if (DataContext is MainViewModel main &&
            main.Player.Engine is LibVlcPlaybackEngine vlc)
        {
            VideoSurface.MediaPlayer = vlc.MediaPlayer;
        }

        // Refresh live tracks/chapters shortly after media starts.
        if (DataContext is MainViewModel m)
            m.Player.RefreshTracks();

        Focus();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel main)
            return;

        var command = PlayerKeyMap.Resolve(e.Key, Keyboard.Modifiers);
        switch (command)
        {
            case PlayerCommand.TogglePlayPause: main.Player.TogglePlayPauseCommand.Execute(null); e.Handled = true; break;
            case PlayerCommand.SeekForward: main.Player.Engine.SeekTo(main.Player.PositionSeconds + 10); e.Handled = true; break;
            case PlayerCommand.SeekBackward: main.Player.Engine.SeekTo(main.Player.PositionSeconds - 10); e.Handled = true; break;
            case PlayerCommand.ToggleFullscreen: main.Player.ToggleFullscreenCommand.Execute(null); e.Handled = true; break;
            case PlayerCommand.ExitFullscreen: main.Player.IsFullscreen = false; e.Handled = true; break;
            case PlayerCommand.Screenshot: main.Player.ScreenshotCommand.Execute(null); e.Handled = true; break;
        }
    }
}
```

- [ ] **Step 3: Host the player pane in MainWindow**

In `src/VideoShelf.App/Views/MainWindow.xaml`, add the namespace `xmlns:views="clr-namespace:VideoShelf.App.Views"` to the root element, and overlay the player pane on top of the browse area. Inside the outer `<Grid Grid.Row="1">` (the one with two columns), add a final child spanning both columns that shows when playing and NOT in PiP:
```xml
            <views:PlayerView Grid.Column="0" Grid.ColumnSpan="2"
                              DataContext="{Binding}"
                              Visibility="{Binding IsPlayerVisible, Converter={StaticResource BoolToVisibility}}" />
```
Place it as the last child of that grid so it renders above the sidebar/content. (PiP visibility is handled by Task 19; when `IsPictureInPicture` is true the inline pane should hide — bind its visibility to a `ShowInlinePlayer` helper or add a `MultiBinding`. Simplest: keep inline visible only when `IsPlayerVisible && !IsPictureInPicture`; add a bool property `IsInlinePlayerVisible => IsPlayerVisible && !IsPictureInPicture` to `MainViewModel` in this task and bind to it.)

Add to `MainViewModel` (this task), updating the two relevant `[ObservableProperty]` change hooks:
```csharp
    public bool IsInlinePlayerVisible => IsPlayerVisible && !IsPictureInPicture;

    partial void OnIsPlayerVisibleChanged(bool value) => OnPropertyChanged(nameof(IsInlinePlayerVisible));
    partial void OnIsPictureInPictureChanged(bool value) => OnPropertyChanged(nameof(IsInlinePlayerVisible));
```
And bind the inline `PlayerView` `Visibility` to `IsInlinePlayerVisible` instead of `IsPlayerVisible`.

- [ ] **Step 4: Verify (build only — integration View, no unit test)**

Run: `cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && dotnet build VideoShelf.slnx -c Release --nologo 2>&1 | tail -15`
Expected: Build succeeded, 0 errors. Then run the full test gate to confirm the new `IsInlinePlayerVisible` did not break VM tests:
Run: `cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && dotnet test VideoShelf.slnx -c Release --nologo -v q 2>&1 | tail -8`
Expected: all green.

> **Phase 6 harness must later screenshot:** the inline player with the overlay bar over a rendering clip; the resume banner on a partially-watched clip; the error banner on a missing file.

- [ ] **Step 5: Commit**

```
cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && git add src/VideoShelf.App/Views/PlayerView.xaml src/VideoShelf.App/Views/PlayerView.xaml.cs src/VideoShelf.App/Views/MainWindow.xaml src/VideoShelf.App/ViewModels/MainViewModel.cs && git commit -m "feat(app): embedded PlayerView (VideoView + additive overlay controls)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 19: App — mini-player / PiP window (detachable, always-on-top)

**Integration component — no unit tests.** A separate small always-on-top `Window` that re-hosts the same `MediaPlayer` when PiP is toggled, so the user keeps watching while browsing. The toggle state (`IsPictureInPicture`) and routing already exist (Task 15); this task adds the window and its show/hide wiring in `MainWindow` code-behind.

**Files:**
- Create: `src/VideoShelf.App/Views/MiniPlayerWindow.xaml`
- Create: `src/VideoShelf.App/Views/MiniPlayerWindow.xaml.cs`
- Modify: `src/VideoShelf.App/Views/MainWindow.xaml.cs` (observe `IsPictureInPicture`, show/hide the window, re-parent the `VideoView`'s MediaPlayer)

- [ ] **Step 1: Create the MiniPlayerWindow XAML**

Create `src/VideoShelf.App/Views/MiniPlayerWindow.xaml`:
```xml
<Window x:Class="VideoShelf.App.Views.MiniPlayerWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vlc="clr-namespace:LibVLCSharp.WPF;assembly=LibVLCSharp.WPF"
        xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
        Title="VideoShelf — Mini player"
        Width="420" Height="260"
        Topmost="True"
        ShowInTaskbar="True"
        Background="Black"
        WindowStartupLocation="Manual">
    <Grid>
        <vlc:VideoView x:Name="MiniSurface" />
        <ui:Button Content="Back to window" VerticalAlignment="Bottom" HorizontalAlignment="Right"
                   Margin="8" Click="OnReturnClick" />
    </Grid>
</Window>
```

- [ ] **Step 2: Create the code-behind**

Create `src/VideoShelf.App/Views/MiniPlayerWindow.xaml.cs`:
```csharp
using System;
using System.Windows;
using LibVLCSharp.Shared;

namespace VideoShelf.App.Views;

/// <summary>Detachable always-on-top mini-player. Re-hosts the shared MediaPlayer while PiP is on.</summary>
public partial class MiniPlayerWindow : Window
{
    /// <summary>Raised when the user clicks "Back to window" (asks the shell to leave PiP).</summary>
    public event EventHandler? ReturnRequested;

    public MiniPlayerWindow(MediaPlayer player)
    {
        InitializeComponent();
        MiniSurface.MediaPlayer = player;
    }

    /// <summary>Detaches the MediaPlayer before closing so the inline VideoView can re-host it.</summary>
    public void DetachPlayer()
    {
        MiniSurface.MediaPlayer = null;
    }

    private void OnReturnClick(object sender, RoutedEventArgs e)
        => ReturnRequested?.Invoke(this, EventArgs.Empty);
}
```

- [ ] **Step 3: Wire show/hide + re-parenting in MainWindow code-behind**

In `src/VideoShelf.App/Views/MainWindow.xaml.cs`, observe `IsPictureInPicture` and move the shared `MediaPlayer` between the inline `VideoView` and the mini window. Replace the file with:
```csharp
using System;
using System.ComponentModel;
using Wpf.Ui.Controls;
using VideoShelf.App.Services;
using VideoShelf.App.ViewModels;
using VideoShelf.App.Views;

namespace VideoShelf.App.Views;

public partial class MainWindow : FluentWindow
{
    private readonly MainViewModel _viewModel;
    private MiniPlayerWindow? _miniPlayer;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Loaded += async (_, _) =>
        {
            try { await _viewModel.InitializeAsync(); }
            catch { /* startup load is best-effort; surfaced via empty UI */ }
        };
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsPictureInPicture))
            UpdatePictureInPicture(_viewModel.IsPictureInPicture);
    }

    private void UpdatePictureInPicture(bool on)
    {
        if (_viewModel.Player.Engine is not LibVlcPlaybackEngine vlc)
            return;

        if (on)
        {
            _miniPlayer = new MiniPlayerWindow(vlc.MediaPlayer);
            _miniPlayer.ReturnRequested += (_, _) => _viewModel.IsPictureInPicture = false;
            _miniPlayer.Closed += (_, _) =>
            {
                // Closing the mini window leaves PiP and returns the player inline.
                if (_viewModel.IsPictureInPicture)
                    _viewModel.IsPictureInPicture = false;
            };
            _miniPlayer.Show();
        }
        else if (_miniPlayer is not null)
        {
            _miniPlayer.DetachPlayer();   // release the MediaPlayer before the inline surface re-hosts it
            var w = _miniPlayer;
            _miniPlayer = null;
            w.Close();
            // The inline PlayerView re-binds the MediaPlayer on its next Loaded; force a refresh by
            // toggling inline visibility is unnecessary — IsInlinePlayerVisible already flips when PiP clears.
        }
    }
}
```

> **Re-parenting note (verify in harness, Phase 6):** A libVLC `MediaPlayer` can be hosted by only ONE `VideoView` at a time. The inline `PlayerView.OnLoaded` assigns `VideoSurface.MediaPlayer = vlc.MediaPlayer`; the mini window assigns the same instance. The handoff order matters: when entering PiP we must clear the inline surface (`VideoSurface.MediaPlayer = null`) before the mini window claims it, and vice-versa on exit. If video freezes/blanks after a toggle in the harness, add explicit `VideoSurface.MediaPlayer = null` on the inline side when PiP turns on (expose a method on `PlayerView` and call it here). This is the single riskiest integration point — see Risks.

- [ ] **Step 4: Verify (build only — integration window, no unit test)**

Run: `cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && dotnet build VideoShelf.slnx -c Release --nologo 2>&1 | tail -15`
Expected: Build succeeded, 0 errors. Then full gate:
Run: `cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && dotnet test VideoShelf.slnx -c Release --nologo -v q 2>&1 | tail -8`
Expected: all green.

> **Phase 6 harness must later screenshot:** the always-on-top mini-player window playing while the main library is visible/scrollable behind it, and a clean handoff back to the inline player on "Back to window".

- [ ] **Step 5: Commit**

```
cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && git add src/VideoShelf.App/Views/MiniPlayerWindow.xaml src/VideoShelf.App/Views/MiniPlayerWindow.xaml.cs src/VideoShelf.App/Views/MainWindow.xaml.cs && git commit -m "feat(app): detachable always-on-top mini-player/PiP window

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 20: App — auto-advance settings toggle in the UI

Spec §9: "Auto-advance can be turned off in settings." Expose the `SettingsRepository.GetAutoAdvanceEpisodes`/`Set` through a small `SettingsViewModel` and a checkbox in `MainWindow` so the toggle is user-reachable (the engine logic already reads the setting in Task 9).

**Files:**
- Create: `src/VideoShelf.App/ViewModels/SettingsViewModel.cs`
- Modify: `src/VideoShelf.App/Services/ServiceCollectionExtensions.cs` (register `SettingsViewModel`; expose on `MainViewModel`)
- Modify: `src/VideoShelf.App/ViewModels/MainViewModel.cs` (add `Settings` accessor + ctor param)
- Modify: `src/VideoShelf.App/Views/MainWindow.xaml` (a checkbox bound to it)
- Test: `tests/VideoShelf.App.Tests/SettingsViewModelTests.cs` (create)

- [ ] **Step 1: Write the failing test**

Create `tests/VideoShelf.App.Tests/SettingsViewModelTests.cs`:
```csharp
using Shouldly;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.Tests;

public class SettingsViewModelTests
{
    [Fact]
    public void AutoAdvance_defaults_true_from_repository()
    {
        using var temp = new AppTempDb();
        var vm = new SettingsViewModel(new SettingsRepository(temp.Db));

        vm.AutoAdvanceEpisodes.ShouldBeTrue();
    }

    [Fact]
    public void Setting_AutoAdvance_false_persists()
    {
        using var temp = new AppTempDb();
        var settings = new SettingsRepository(temp.Db);
        var vm = new SettingsViewModel(settings);

        vm.AutoAdvanceEpisodes = false;

        settings.GetAutoAdvanceEpisodes().ShouldBeFalse();
        new SettingsViewModel(settings).AutoAdvanceEpisodes.ShouldBeFalse();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && dotnet test tests/VideoShelf.App.Tests/VideoShelf.App.Tests.csproj -c Release --nologo -v q 2>&1 | tail -20`
Expected: FAIL — type `SettingsViewModel` does not exist.

- [ ] **Step 3: Write minimal implementation**

(a) Create `src/VideoShelf.App/ViewModels/SettingsViewModel.cs`:
```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsRepository _settings;

    public SettingsViewModel(SettingsRepository settings)
    {
        _settings = settings;
        _autoAdvanceEpisodes = settings.GetAutoAdvanceEpisodes();
    }

    [ObservableProperty]
    private bool _autoAdvanceEpisodes;

    partial void OnAutoAdvanceEpisodesChanged(bool value)
        => _settings.SetAutoAdvanceEpisodes(value);
}
```

(b) In `src/VideoShelf.App/ViewModels/MainViewModel.cs`, add a `SettingsViewModel` ctor param and accessor. Add the field, ctor param, and property:
```csharp
    private readonly SettingsViewModel _settings;
```
Extend the constructor signature to include `SettingsViewModel settings` and assign `_settings = settings;`, then add:
```csharp
    public SettingsViewModel Settings => _settings;
```

(c) In `src/VideoShelf.App/Services/ServiceCollectionExtensions.cs`, register it before `MainViewModel`:
```csharp
        services.AddSingleton<SettingsViewModel>();
```

(d) Update any test that constructs `MainViewModel` directly (e.g. `MainViewModelPlaybackTests.Make` and any in `MainViewModelTests`) to pass a `SettingsViewModel`. In `MainViewModelPlaybackTests.Make`, build `var settingsVm = new SettingsViewModel(settings);` and pass it as the last ctor argument.

(e) In `src/VideoShelf.App/Views/MainWindow.xaml`, add a checkbox in the sources sidebar (additive), e.g. below the Add source / Scan buttons:
```xml
                    <CheckBox DockPanel.Dock="Top" Margin="0,8,0,0"
                              Content="Auto-play next episode"
                              IsChecked="{Binding Settings.AutoAdvanceEpisodes}" />
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && dotnet test VideoShelf.slnx -c Release --nologo -v q 2>&1 | tail -8`
Expected: PASS — full suite green (App test count now 79).

- [ ] **Step 5: Commit**

```
cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && git add src/VideoShelf.App/ViewModels/SettingsViewModel.cs src/VideoShelf.App/ViewModels/MainViewModel.cs src/VideoShelf.App/Services/ServiceCollectionExtensions.cs src/VideoShelf.App/Views/MainWindow.xaml tests/VideoShelf.App.Tests/SettingsViewModelTests.cs tests/VideoShelf.App.Tests/MainViewModelPlaybackTests.cs tests/VideoShelf.App.Tests/MainViewModelTests.cs && git commit -m "feat(app): auto-advance settings toggle (VM + checkbox)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 21: Full-suite verification gate

**Files:** none (verification only).

- [ ] **Step 1: Run the complete test gate**

Run: `cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && dotnet test VideoShelf.slnx -c Release --nologo -v q 2>&1 | tail -10`
Expected: Build succeeds; **0 failures**. Totals: Core 60 (45 baseline + 4 resume + 2 watched-clears + 4 next-episode + 5 settings) and App ~79 (26 baseline + new tests across Tasks 5–20). The exact App total may differ by a few; what matters is **0 failed** and that every new test from Tasks 1–20 is present and green.

- [ ] **Step 2: Confirm Release build is clean (View/engine/PiP integration components compile)**

Run: `cd "C:/Agent Projects/VideoShelf/.worktrees/feat-playback" && dotnet build VideoShelf.slnx -c Release --nologo 2>&1 | tail -6`
Expected: Build succeeded, 0 Warning(s) related to our code, 0 Error(s).

- [ ] **Step 3: No commit** — verification only. The controller proceeds to push/PR/merge per the runbook (§5).

---

## Self-Review (run by the plan author; fixes already folded in)

**1. Spec coverage (§9 + relevant §6):**
- Embedded VideoView + overlay control bar → Task 18.
- Play/pause + draggable seek bar (current/total time) → Task 8 (transport) + Task 18 (slider/time).
- Seek-preview thumbnails (#2) → Task 12 (`RequestSeekPreviewAsync`, cached, fail-safe) + engine in Task 17; View hover wiring noted for harness (preview overlay is additive, exercised in Phase 6).
- Volume → Task 10.
- Fullscreen toggle + Esc exit → Task 10 (`IsFullscreen`) + Task 11 (key map) + Task 18 (button/keys).
- Embedded subtitle + audio pickers incl. "subtitles off" → Task 10 (mapping) + Task 17 (live enumeration, Off injected) + Task 18 (combos).
- Chapter navigation (#7), render nothing when none → Task 10 (`HasChapters`, next/prev) + Task 17 (enumeration) + Task 18 (conditional buttons).
- Frame capture (#8) `Ctrl+E` → capture folder → Task 12 (command, capture-folder only) + Task 11 (key) + Task 17 (snapshot).
- Keyboard shortcuts → Task 11 + Task 18.
- Mini-player / PiP → Task 19.
- Resume / continue-watching (#5): periodic save, offer on reopen, clear on watched → Tasks 1, 2, 7, 8.
- Auto-mark watched on end (clears resume) → Task 9.
- Auto-play next within series (#10), settings toggle, standalones never → Tasks 3, 4, 9, 15, 20.
- Out-of-scope respected: no playback-speed, no sidecars/downloads, no whole-library queue (auto-next is in-series only via `GetNextEpisode` with `is_standalone = 0`).
- §6 missing-file "file not found instead of failing" on play → Task 13.
- Read-only/self-contained/fail-safe/crash-safe: snapshots/previews write only to capture/preview dirs; engine wrapped in try/catch surfacing `EncounteredError`; no external tools/PATH/network introduced.

**2. Placeholder scan:** No "TBD/similar to above/add error handling" — every code step has full code. View/engine/PiP tasks have explicit XAML/C# and a build gate (allowed: they are unit-untestable integration components, per the architecture guidance).

**3. Type/name consistency:** `IPlaybackEngine`, `TrackOption(Id,Label)` + `SubtitlesOffId`, `ChapterOption(Index,Name)`, `FakePlaybackEngine`, `ResumePolicy`, `PlayerViewModel` (`Open`/`FlushResume`/`NextEpisodeRequested`/`RefreshTracks`/`Volume`/`ScreenshotCommand`/`RequestSeekPreviewAsync`/`PlaybackError`/`CaptureDirectory`/`SeekPreviewDirectory`/`RaiseNextEpisodeForTest`), `PlayerKeyMap.Resolve`/`PlayerCommand`, `SettingsRepository` (`GetAutoAdvanceEpisodes`/`SetAutoAdvanceEpisodes`/`GetString`/`SetString`/`AutoAdvanceKey`), `LibraryRepository` (`GetResumePosition`/`SetResumePosition`/`ClearResumePosition`/`GetNextEpisode`), `WatchRepository.SetWatched` (clears resume), `LibVlcPlaybackEngine.MediaPlayer`, `MiniPlayerWindow(MediaPlayer)`/`DetachPlayer`/`ReturnRequested`, `MainViewModel` (`PlayEpisode`/`IsPlayerVisible`/`IsInlinePlayerVisible`/`IsPictureInPicture`/`TogglePictureInPictureCommand`/`ClosePlayerCommand`/`Player`/`Settings`). Cross-task references all match.

**Execution-order caveat folded into tasks:** Task 17 (`LibVlcPlaybackEngine`) must be implemented before Task 16 (DI references it). Both noted inline.
