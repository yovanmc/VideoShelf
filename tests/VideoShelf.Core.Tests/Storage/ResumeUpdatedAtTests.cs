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
