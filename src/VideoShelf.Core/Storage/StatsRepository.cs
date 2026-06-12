using System;
using System.Collections.Generic;
using VideoShelf.Core.Models;

namespace VideoShelf.Core.Storage;

public sealed class StatsRepository(VideoShelfDb db)
{
    public LibraryStats GetLibraryStats()
    {
        using var conn = db.Open();

        int RunCount(string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            return Convert.ToInt32(cmd.ExecuteScalar()!);
        }

        var total      = RunCount("SELECT COUNT(*) FROM videos WHERE missing = 0");
        var watched    = RunCount("SELECT COUNT(*) FROM videos WHERE watched = 1 AND missing = 0");
        var inProgress = RunCount("SELECT COUNT(*) FROM videos WHERE resume_position IS NOT NULL AND missing = 0");

        using var durCmd = conn.CreateCommand();
        durCmd.CommandText = "SELECT COALESCE(SUM(duration), 0) FROM videos WHERE watched = 1 AND missing = 0";
        var watchedSecs = Convert.ToDouble(durCmd.ExecuteScalar()!);

        return new LibraryStats(total, watched, inProgress, watchedSecs);
    }

    public IReadOnlyList<CreatorWatchCount> GetTopCreatorsByWatched(int limit)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT s.id, s.display_name, COUNT(v.id) AS c
              FROM sections s
              JOIN series  se ON se.section_id = s.id
              JOIN videos  v  ON v.series_id   = se.id
             WHERE v.watched = 1 AND v.missing = 0
             GROUP BY s.id, s.display_name
            HAVING c > 0
             ORDER BY c DESC, s.display_name ASC
             LIMIT $limit
            """;
        cmd.Parameters.AddWithValue("$limit", limit);
        var list = new List<CreatorWatchCount>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new CreatorWatchCount(r.GetInt64(0), r.GetString(1), r.GetInt32(2)));
        return list;
    }
}
