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
}
