using System;
using System.Collections.Generic;
using VideoShelf.Core.Models;

namespace VideoShelf.Core.Storage;

public sealed class StatsRepository(VideoShelfDb db)
{
    // ── E1 ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a rating bucket for each distinct half-star step (0.5 increments) that has
    /// at least one video, plus a bucket for unrated videos (rating = 0).  Videos with
    /// NULL or 0 rating are counted in the 0-bucket.  Results are ordered rating ascending.
    /// </summary>
    public IReadOnlyList<RatingBucket> GetRatingDistribution()
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        // rating is stored as REAL (double); group by the raw value.
        // The schema stores 0 for "unrated" (EnsureColumn default = 0, REAL affinity).
        cmd.CommandText = """
            SELECT COALESCE(rating, 0) AS r, COUNT(*) AS c
              FROM videos
             WHERE missing = 0
             GROUP BY r
             ORDER BY r ASC
            """;
        var list = new List<RatingBucket>();
        using var rdr = cmd.ExecuteReader();
        while (rdr.Read())
            list.Add(new RatingBucket(rdr.GetDouble(0), rdr.GetInt32(1)));
        return list;
    }

    /// <summary>
    /// Returns one data point per calendar month (strftime '%Y-%m') for the last
    /// <paramref name="months"/> months, counting watch events in each month.
    /// Months with no events are omitted.  Results are ordered period ascending.
    /// </summary>
    public IReadOnlyList<WatchActivityPoint> GetWatchActivityByMonth(int months)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        // watched_at is stored as ISO-8601 TEXT (inserted by WatchRepository).
        // We take the last $months calendar months relative to the current date.
        cmd.CommandText = """
            SELECT strftime('%Y-%m', watched_at) AS period, COUNT(*) AS c
              FROM watch_events
             WHERE watched_at >= strftime('%Y-%m-%d', 'now', $offset)
             GROUP BY period
             ORDER BY period ASC
            """;
        cmd.Parameters.AddWithValue("$offset", $"-{months} months");
        var list = new List<WatchActivityPoint>();
        using var rdr = cmd.ExecuteReader();
        while (rdr.Read())
            list.Add(new WatchActivityPoint(rdr.GetString(0), rdr.GetInt32(1)));
        return list;
    }

    /// <summary>
    /// Returns the top <paramref name="limit"/> video-level tags by total video count,
    /// each annotated with how many of those videos are watched.
    /// Uses the <c>video_tags</c> canonical table.
    /// Results are ordered total descending.
    /// </summary>
    public IReadOnlyList<TagWatchStat> GetTopTagsByWatch(int limit)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT vt.tag,
                   COUNT(v.id)                               AS total,
                   SUM(CASE WHEN v.watched = 1 THEN 1 ELSE 0 END) AS watched
              FROM video_tags vt
              JOIN videos v ON v.id = vt.video_id
             WHERE v.missing = 0
             GROUP BY vt.tag
             ORDER BY total DESC, vt.tag ASC
             LIMIT $limit
            """;
        cmd.Parameters.AddWithValue("$limit", limit);
        var list = new List<TagWatchStat>();
        using var rdr = cmd.ExecuteReader();
        while (rdr.Read())
            list.Add(new TagWatchStat(rdr.GetString(0), rdr.GetInt32(1), rdr.GetInt32(2)));
        return list;
    }

    /// <summary>
    /// Returns counts of creators (sections), series, standalone series, total non-missing
    /// videos, and the sum of all non-missing video durations.
    /// </summary>
    public LibraryComposition GetLibraryComposition()
    {
        using var conn = db.Open();

        int RunScalar(string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            return Convert.ToInt32(cmd.ExecuteScalar()!);
        }

        var creators    = RunScalar("SELECT COUNT(*) FROM sections");
        var series      = RunScalar("SELECT COUNT(*) FROM series WHERE is_standalone = 0");
        var standalones = RunScalar("SELECT COUNT(*) FROM series WHERE is_standalone = 1");

        using var durCmd = conn.CreateCommand();
        durCmd.CommandText = """
            SELECT COUNT(*), COALESCE(SUM(duration), 0)
              FROM videos
             WHERE missing = 0
            """;
        using var rdr = durCmd.ExecuteReader();
        rdr.Read();
        var totalVideos = rdr.GetInt32(0);
        var totalDur    = rdr.GetDouble(1);

        return new LibraryComposition(creators, series, standalones, totalVideos, totalDur);
    }
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
