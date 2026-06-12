using System.Globalization;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;

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
            LIMIT $limit;
            """;
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$limit", limit);
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
            LIMIT $limit;
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
            LIMIT $limit;
            """, limit);

    private IReadOnlyList<RecencyItem> ReadRecency(string sql, int limit)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$limit", limit);
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

    public IReadOnlyList<SectionSuggestion> GetForYou(int limit, DateTimeOffset now) =>
        ScoreSections(now).Take(limit).ToList();

    private List<SectionSuggestion> ScoreSections(DateTimeOffset now)
    {
        var history = ReadWatchedTags();
        if (history.Count == 0) return [];
        var affinity = DiscoveryScoring.BuildTagAffinity(history, now, HalfLifeDays);
        var watchedSections = ReadWatchedSectionIds();

        var scored = new List<SectionSuggestion>();
        foreach (var sec in ReadSectionStats())
        {
            if (watchedSections.Contains(sec.SectionId)) continue;
            var secTags = tags.GetTags(sec.SectionId);
            var score = DiscoveryScoring.ScoreSection(secTags, affinity, sec.UnwatchedCount, sec.EpisodeCount);
            if (score <= 0) continue;
            scored.Add(sec with { Tags = secTags, Score = score });
        }
        return scored
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<RecencyItem> GetRecommendedVideos(int limit, DateTimeOffset now)
    {
        var sections = ScoreSections(now);
        if (sections.Count == 0) return [];

        // Map each recommended section to its rank (recommendation order).
        var rank = new Dictionary<long, int>();
        for (var i = 0; i < sections.Count; i++) rank[sections[i].SectionId] = i;

        // section ids are trusted integer keys from our own DB (never user input), so an
        // inlined IN-list is safe here and avoids dynamic LIKE/param plumbing.
        var ids = string.Join(",", rank.Keys);
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT v.id, v.series_id, s.section_id, s.base_title, s.is_standalone,
                   v.episode_no, v.watched, v.thumbnail_path
            FROM videos v
            JOIN series s ON s.id = v.series_id
            WHERE v.missing = 0 AND v.watched = 0 AND s.section_id IN ({ids})
            ORDER BY v.added_at DESC, v.id DESC;
            """;
        var all = new List<RecencyItem>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            all.Add(new RecencyItem(
                VideoId: r.GetInt64(0), SeriesId: r.GetInt64(1), SectionId: r.GetInt64(2),
                SeriesTitle: r.GetString(3), IsStandalone: r.GetInt64(4) != 0,
                EpisodeNo: r.GetInt32(5), Watched: r.GetInt64(6) != 0,
                ThumbnailSeedPath: r.IsDBNull(7) ? null : r.GetString(7)));

        return all
            .OrderBy(v => rank[v.SectionId])
            .ThenByDescending(v => v.VideoId)
            .Take(limit)
            .ToList();
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

    private List<WatchedTag> ReadWatchedTags()
    {
        const string sql = """
            SELECT st.tag, we.watched_at
            FROM watch_events we
            JOIN videos v ON v.id = we.video_id
            JOIN series s ON s.id = v.series_id
            JOIN section_tags st ON st.section_id = s.section_id
            ORDER BY we.watched_at DESC
            LIMIT $window;
            """;
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$window", HistoryWindow);
        var list = new List<WatchedTag>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new WatchedTag(r.GetString(0), DateTimeOffset.Parse(r.GetString(1),
                null, DateTimeStyles.RoundtripKind)));
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
