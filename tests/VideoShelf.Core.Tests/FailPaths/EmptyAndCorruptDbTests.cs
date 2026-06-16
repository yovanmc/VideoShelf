using System;
using System.IO;
using Microsoft.Data.Sqlite;
using Shouldly;
using VideoShelf.Core.Storage;
using Xunit;

namespace VideoShelf.Core.Tests.FailPaths;

/// <summary>
/// C4 — an empty library and a busy/locked DB must survive: every read repo returns empty
/// collections (never throws) on a brand-new DB, Migrate() is idempotent across reopens, and a
/// concurrent WAL lock is ridden out by busy_timeout instead of surfacing an unhandled SQLITE_BUSY.
/// </summary>
public sealed class EmptyAndCorruptDbTests : IDisposable
{
    private readonly string _path;
    private readonly VideoShelfDb _db;

    public EmptyAndCorruptDbTests()
    {
        _path = Path.Combine(Path.GetTempPath(), "vshelf_c4_" + Guid.NewGuid().ToString("N") + ".db");
        _db = new VideoShelfDb(_path);
        _db.Migrate();
    }

    // ── (a) empty DB: every read repo returns empty, never throws ──────────────

    [Fact]
    public void Empty_db_read_repos_return_empty_and_never_throw()
    {
        var lib     = new LibraryRepository(_db);
        var maint   = new MaintenanceRepository(_db);
        var history = new HistoryRepository(_db);
        var play    = new PlaylistRepository(_db);
        var stats   = new StatsRepository(_db);
        var tags    = new TagRepository(_db);
        var watch   = new WatchRepository(_db);
        var curation = new CurationRepository(_db);

        // Library
        lib.GetSources().ShouldBeEmpty();
        lib.GetSectionSummaries().ShouldBeEmpty();
        lib.GetVideosForSeries(1).ShouldBeEmpty();
        lib.GetSections(1).ShouldBeEmpty();
        lib.GetSeriesForSection(1).ShouldBeEmpty();
        lib.GetEpisodes(1).ShouldBeEmpty();
        lib.GetEpisodesForSection(1).ShouldBeEmpty();
        lib.Search("anything").ShouldBeEmpty();
        lib.SearchCreators("x", 10).ShouldBeEmpty();
        lib.SearchSeries("x", 10).ShouldBeEmpty();
        lib.SearchVideos("x", 10).ShouldBeEmpty();
        lib.GetVideosNeedingDuration().ShouldBeEmpty();
        lib.GetVideosNeedingResolution().ShouldBeEmpty();

        // Maintenance
        maint.GetDuplicateGroups().ShouldBeEmpty();
        maint.GetDuplicateGroupsForSection(1).ShouldBeEmpty();
        maint.GetMissingVideos().ShouldBeEmpty();
        maint.GetOrphanSeries().ShouldBeEmpty();
        maint.GetEmptyCreators().ShouldBeEmpty();
        maint.GetDismissedPairs().ShouldBeEmpty();

        // History / playlists / stats / tags / curation
        history.GetHistory(50).ShouldBeEmpty();
        play.GetAll().ShouldBeEmpty();
        play.GetItems(1).ShouldBeEmpty();
        stats.GetRatingDistribution().ShouldNotBeNull();
        stats.GetWatchActivityByMonth(12).ShouldNotBeNull();
        stats.GetTopTagsByWatch(10).ShouldBeEmpty();
        stats.GetTopCreatorsByWatched(10).ShouldBeEmpty();
        tags.GetAllTags().ShouldBeEmpty();
        tags.GetTagCounts().ShouldBeEmpty();
        watch.RecentlyWatchedVideoIds(10).ShouldBeEmpty();
        watch.IsWatched(1).ShouldBeFalse();
        curation.GetWatchlist(10).ShouldBeEmpty();
        curation.GetFavorites(10).ShouldBeEmpty();
    }

    // ── (b) Migrate() is idempotent across consecutive opens of the same file ──

    [Fact]
    public void Migrate_is_idempotent_across_reopens()
    {
        // First migration already ran in the ctor. Run again on the same handle...
        Should.NotThrow(() => _db.Migrate());

        // ...and again from a brand-new VideoShelfDb over the SAME file.
        using var reopened = new VideoShelfDb(_path);
        Should.NotThrow(() => reopened.Migrate());

        // Schema is intact and queryable after the repeated migrations.
        var lib = new LibraryRepository(reopened);
        lib.GetSources().ShouldBeEmpty();
    }

    // ── (c) WAL-busy: a concurrent writer lock is ridden out, not thrown ───────

    [Fact]
    public void Read_repo_survives_a_concurrent_write_lock_via_busy_timeout()
    {
        var lib = new LibraryRepository(_db);

        // Hold an EXCLUSIVE write transaction open on a second connection to the same file.
        using var blocker = _db.Open();
        using var tx = blocker.BeginTransaction();
        using (var cmd = blocker.CreateCommand())
        {
            cmd.Transaction = tx;
            // Take a reserved/write lock by mutating a row.
            cmd.CommandText = "INSERT INTO settings(key, value) VALUES('c4_lock', '1')";
            cmd.ExecuteNonQuery();
        }

        // A reader must still succeed (WAL allows concurrent reads); critically, no unhandled
        // throw escapes the read repo. busy_timeout=5000 covers any momentary contention.
        Should.NotThrow(() => lib.GetSources().ShouldBeEmpty());

        tx.Rollback();
    }

    [Fact]
    public void Open_connection_has_busy_timeout_set()
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA busy_timeout";
        var value = Convert.ToInt64(cmd.ExecuteScalar());
        value.ShouldBeGreaterThan(0, "busy_timeout must be set so contended opens retry instead of throwing");
    }

    public void Dispose()
    {
        _db.Dispose();
        try { File.Delete(_path); } catch { }
        try { File.Delete(_path + "-wal"); } catch { }
        try { File.Delete(_path + "-shm"); } catch { }
    }
}
