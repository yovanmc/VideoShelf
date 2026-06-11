using Microsoft.Data.Sqlite;
using Shouldly;
using VideoShelf.Core.Tests.TestSupport;

namespace VideoShelf.Core.Tests.Storage;

public class VideoShelfDbTests
{
    [Fact]
    public void Migrate_creates_expected_tables()
    {
        using var temp = new TempDb();
        using var conn = temp.Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name";
        var tables = new List<string>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) tables.Add(reader.GetString(0));

        foreach (var expected in new[]
                 { "sources", "sections", "series", "videos", "section_tags", "watch_events", "grouping_overrides", "settings" })
            tables.ShouldContain(expected);
    }

    [Fact]
    public void Migrate_is_idempotent()
    {
        using var temp = new TempDb();
        Should.NotThrow(() => temp.Db.Migrate()); // second migrate is a no-op
    }
}
