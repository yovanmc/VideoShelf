using System;
using System.IO;
using Microsoft.Data.Sqlite;
using Shouldly;
using VideoShelf.Core.Storage;
using Xunit;

namespace VideoShelf.Core.Tests.Storage;

/// <summary>
/// M25 Group D — the first PRAGMA user_version migration. v1 DROPs the smart_views table, which
/// M24 (#104) orphaned end-to-end (zero live readers remain). The runner must be idempotent across
/// reopens (user_version does not climb), and must clean a pre-M25 install that still carries the
/// table. user_version is per-file-and-persisted, so these tests use real temp file paths.
/// </summary>
public sealed class UserVersionMigrationTests : IDisposable
{
    private readonly string _path;

    public UserVersionMigrationTests()
        => _path = Path.Combine(Path.GetTempPath(), "vshelf_m25d_" + Guid.NewGuid().ToString("N") + ".db");

    private static bool TableExists(SqliteConnection conn, string name)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name=@n";
        cmd.Parameters.AddWithValue("@n", name);
        return cmd.ExecuteScalar() is not null;
    }

    private static long UserVersion(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA user_version";
        return (long)(cmd.ExecuteScalar() ?? 0L);
    }

    [Fact]
    public void Migrate_DropsSmartViews_AndSetsUserVersion()
    {
        using var db = new VideoShelfDb(_path);
        db.Migrate();

        using var conn = db.Open();
        TableExists(conn, "smart_views").ShouldBeFalse("v1 migration must drop the M24-orphaned smart_views");
        UserVersion(conn).ShouldBeGreaterThanOrEqualTo(1);

        // Active tables must survive untouched.
        TableExists(conn, "playlists").ShouldBeTrue();
        TableExists(conn, "video_art").ShouldBeTrue();
        TableExists(conn, "dismissed_duplicates").ShouldBeTrue();
    }

    [Fact]
    public void Migrate_IsIdempotent_AcrossReopen()
    {
        using (var db = new VideoShelfDb(_path))
            db.Migrate();

        using (var reopened = new VideoShelfDb(_path))
            Should.NotThrow(() => reopened.Migrate());

        using var db2 = new VideoShelfDb(_path);
        using var conn = db2.Open();
        UserVersion(conn).ShouldBe(1, "user_version must not climb past the latest schema version");
        TableExists(conn, "smart_views").ShouldBeFalse();
    }

    [Fact]
    public void Migrate_PreExistingDbWithSmartViews_GetsCleaned()
    {
        // Simulate a pre-M25 install: a DB that still carries smart_views with user_version 0.
        using (var seed = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _path,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString()))
        {
            seed.Open();
            using var cmd = seed.CreateCommand();
            cmd.CommandText = "CREATE TABLE smart_views(id INTEGER PRIMARY KEY); PRAGMA user_version=0;";
            cmd.ExecuteNonQuery();
        }
        // Drop the shared-cache pool handle so the file is fully flushed before reopening.
        SqliteConnection.ClearAllPools();

        using var db = new VideoShelfDb(_path);
        db.Migrate();

        using var conn = db.Open();
        TableExists(conn, "smart_views").ShouldBeFalse("upgrading a pre-M25 install must drop smart_views");
        UserVersion(conn).ShouldBe(1);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch { }
        try { File.Delete(_path + "-wal"); } catch { }
        try { File.Delete(_path + "-shm"); } catch { }
    }
}
