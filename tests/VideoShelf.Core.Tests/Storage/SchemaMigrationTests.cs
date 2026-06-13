using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Shouldly;
using VideoShelf.Core.Tests.TestSupport;

namespace VideoShelf.Core.Tests.Storage;

public class SchemaMigrationTests
{
    private static HashSet<string> VideoColumns(TempDb temp)
    {
        using var conn = temp.Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA table_info(videos)";
        var cols = new HashSet<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) cols.Add(r.GetString(1)); // column 1 = name
        return cols;
    }

    [Fact]
    public void Migrate_adds_missing_added_at_and_resume_position_columns()
    {
        using var temp = new TempDb();

        var cols = VideoColumns(temp);

        cols.ShouldContain("missing");
        cols.ShouldContain("added_at");
        cols.ShouldContain("resume_position");
    }

    [Fact]
    public void Migrate_is_idempotent_when_run_twice()
    {
        using var temp = new TempDb();

        // Second migrate must not throw "duplicate column".
        Should.NotThrow(() => temp.Db.Migrate());

        var cols = VideoColumns(temp);
        cols.ShouldContain("missing");
    }

    [Fact]
    public void Migrate_creates_video_chapters_table_and_duration_column()
    {
        using var temp = new TempDb();

        // Assert duration column exists in videos table
        var cols = VideoColumns(temp);
        cols.ShouldContain("duration");

        // Assert video_chapters table exists
        using var conn = temp.Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='video_chapters'";
        using var reader = cmd.ExecuteReader();
        reader.Read().ShouldBeTrue("video_chapters table should exist");
        reader.GetString(0).ShouldBe("video_chapters");
    }

    [Fact]
    public void Migrate_adds_is_favorite_and_rating_columns()
    {
        using var temp = new TempDb();
        var cols = VideoColumns(temp);
        cols.ShouldContain("is_favorite");
        cols.ShouldContain("rating");
    }
}
