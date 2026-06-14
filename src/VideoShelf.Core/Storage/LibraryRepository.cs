using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;
using VideoShelf.Core.Discovery;
using VideoShelf.Core.Models;
using VideoShelf.Core.Naming;

namespace VideoShelf.Core.Storage;

/// <summary>Reads/writes sources, sections, series, and videos. Upserts are idempotent by natural key.</summary>
public sealed class LibraryRepository(VideoShelfDb db)
{
    public long UpsertSource(string rootPath, string displayName)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO sources(root_path, display_name) VALUES($p, $n)
            ON CONFLICT(root_path) DO UPDATE SET display_name=excluded.display_name
            RETURNING id;
            """;
        cmd.Parameters.AddWithValue("$p", rootPath);
        cmd.Parameters.AddWithValue("$n", displayName);
        return (long)cmd.ExecuteScalar()!;
    }

    public long UpsertSection(long sourceId, string folderName)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO sections(source_id, folder_name, display_name) VALUES($s, $f, $f)
            ON CONFLICT(source_id, folder_name) DO UPDATE SET folder_name=excluded.folder_name
            RETURNING id;
            """;
        cmd.Parameters.AddWithValue("$s", sourceId);
        cmd.Parameters.AddWithValue("$f", folderName);
        return (long)cmd.ExecuteScalar()!;
    }

    public long UpsertSeries(long sectionId, string baseTitle, bool isStandalone)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO series(section_id, base_title, sort_key, is_standalone) VALUES($s, $b, $k, $a)
            ON CONFLICT(section_id, base_title) DO UPDATE SET is_standalone=excluded.is_standalone
            RETURNING id;
            """;
        cmd.Parameters.AddWithValue("$s", sectionId);
        cmd.Parameters.AddWithValue("$b", baseTitle);
        cmd.Parameters.AddWithValue("$k", baseTitle.ToLowerInvariant());
        cmd.Parameters.AddWithValue("$a", isStandalone ? 1 : 0);
        return (long)cmd.ExecuteScalar()!;
    }

    /// <summary>
    /// Upserts a video row keyed by <paramref name="filePath"/>.
    /// <paramref name="sizeBytes"/> is optional (nullable-trailing pattern); when non-null it is
    /// written to <c>size_bytes</c> on both INSERT and UPDATE so the column stays current.
    /// </summary>
    public long UpsertVideo(long seriesId, string filePath, int episodeNo, string format, long? sizeBytes = null)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        if (sizeBytes.HasValue)
        {
            cmd.CommandText = """
                INSERT INTO videos(series_id, file_path, episode_no, raw_filename, format, added_at, missing, size_bytes)
                VALUES($s, $p, $e, $r, $f, $at, 0, $sz)
                ON CONFLICT(file_path) DO UPDATE SET series_id=excluded.series_id,
                    episode_no=excluded.episode_no, raw_filename=excluded.raw_filename,
                    format=excluded.format, size_bytes=excluded.size_bytes
                RETURNING id;
                """;
            cmd.Parameters.AddWithValue("$sz", sizeBytes.Value);
        }
        else
        {
            cmd.CommandText = """
                INSERT INTO videos(series_id, file_path, episode_no, raw_filename, format, added_at, missing)
                VALUES($s, $p, $e, $r, $f, $at, 0)
                ON CONFLICT(file_path) DO UPDATE SET series_id=excluded.series_id,
                    episode_no=excluded.episode_no, raw_filename=excluded.raw_filename,
                    format=excluded.format
                RETURNING id;
                """;
        }
        cmd.Parameters.AddWithValue("$s", seriesId);
        cmd.Parameters.AddWithValue("$p", filePath);
        cmd.Parameters.AddWithValue("$e", episodeNo);
        cmd.Parameters.AddWithValue("$r", System.IO.Path.GetFileName(filePath));
        cmd.Parameters.AddWithValue("$f", format);
        cmd.Parameters.AddWithValue("$at", System.DateTimeOffset.UtcNow.ToString("o"));
        return (long)cmd.ExecuteScalar()!;
    }

    public IReadOnlyList<Source> GetSources()
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, root_path, display_name FROM sources ORDER BY display_name";
        var list = new List<Source>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new Source(r.GetInt64(0), r.GetString(1), r.GetString(2)));
        return list;
    }

    public IReadOnlyList<Video> GetVideosForSeries(long seriesId)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, series_id, file_path, episode_no, raw_filename, format, duration,
                   thumbnail_path, watched, added_at, missing, size_bytes
            FROM videos WHERE series_id=$s ORDER BY episode_no
            """;
        cmd.Parameters.AddWithValue("$s", seriesId);
        var list = new List<Video>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new Video(
                r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetInt32(3), r.GetString(4),
                r.GetString(5), r.IsDBNull(6) ? null : r.GetDouble(6),
                r.IsDBNull(7) ? null : r.GetString(7), r.GetInt64(8) != 0,
                r.IsDBNull(9) ? "" : r.GetString(9), r.GetInt64(10) != 0,
                r.IsDBNull(11) ? null : r.GetInt64(11)));
        return list;
    }

    public IReadOnlyList<Section> GetSections(long sourceId)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, source_id, folder_name, display_name FROM sections WHERE source_id=$s ORDER BY display_name";
        cmd.Parameters.AddWithValue("$s", sourceId);
        var list = new List<Section>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new Section(r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetString(3)));
        return list;
    }

    public Section? GetSection(long id)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, source_id, folder_name, display_name FROM sections WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new Section(r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetString(3));
    }

    public IReadOnlyList<Series> GetSeriesForSection(long sectionId)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, section_id, base_title, sort_key, is_standalone FROM series WHERE section_id=$s ORDER BY sort_key";
        cmd.Parameters.AddWithValue("$s", sectionId);
        var list = new List<Series>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new Series(r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetString(3), r.GetInt64(4) != 0));
        return list;
    }

    /// <summary>Returns the series row for a given id, or null if not found.</summary>
    public Series? GetSeries(long seriesId)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, section_id, base_title, sort_key, is_standalone FROM series WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", seriesId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new Series(r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetString(3), r.GetInt64(4) != 0);
    }

    /// <summary>Marks every video under the given source as missing (a scan will clear the ones it finds).</summary>
    public void MarkAllMissingForSource(long sourceId)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE videos SET missing = 1
            WHERE series_id IN (
                SELECT se.id FROM series se
                JOIN sections sc ON sc.id = se.section_id
                WHERE sc.source_id = $src)
            """;
        cmd.Parameters.AddWithValue("$src", sourceId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Clears the missing flag for a single video by file path (called when a scan finds it).</summary>
    public void ClearMissing(string filePath)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE videos SET missing = 0 WHERE file_path = $p";
        cmd.Parameters.AddWithValue("$p", filePath);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<SectionSummary> GetSectionSummaries()
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT sc.id, sc.source_id, sc.display_name,
                   COUNT(DISTINCT se.id) AS series_count,
                   COALESCE(SUM(CASE WHEN v.id IS NOT NULL AND v.watched = 0 THEN 1 ELSE 0 END), 0) AS unwatched,
                   COUNT(v.id) AS video_count,
                   (SELECT v2.file_path
                      FROM videos v2
                      JOIN series se2 ON se2.id = v2.series_id
                     WHERE se2.section_id = sc.id AND v2.missing = 0
                     ORDER BY se2.id, v2.episode_no
                     LIMIT 1) AS seed_path
            FROM sections sc
            LEFT JOIN series se ON se.section_id = sc.id
            LEFT JOIN videos v ON v.series_id = se.id
            GROUP BY sc.id, sc.source_id, sc.display_name
            ORDER BY sc.display_name
            """;
        var list = new List<SectionSummary>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new SectionSummary(
                SectionId: r.GetInt64(0),
                SourceId: r.GetInt64(1),
                DisplayName: r.GetString(2),
                SeriesCount: r.GetInt32(3),
                UnwatchedCount: r.GetInt32(4),
                VideoCount: r.GetInt32(5),
                ThumbnailSeedPath: r.IsDBNull(6) ? null : r.GetString(6)));
        return list;
    }

    public IReadOnlyList<SeriesSummary> GetSeriesSummaries(long sectionId)
        => GetSeriesSummaries(sectionId, BrowseSort.Name);

    public IReadOnlyList<SeriesSummary> GetSeriesSummaries(long sectionId, BrowseSort sort)
    {
        var orderBy = sort switch
        {
            BrowseSort.DateAdded =>
                "(SELECT MAX(added_at) FROM videos vv WHERE vv.series_id = se.id) DESC, se.sort_key",
            BrowseSort.RecentlyWatched =>
                "(SELECT MAX(we.watched_at) FROM watch_events we " +
                "JOIN videos vv ON vv.id = we.video_id WHERE vv.series_id = se.id) DESC, se.sort_key",
            _ => "se.sort_key",
        };

        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT se.id, se.section_id, se.base_title, se.is_standalone,
                   COUNT(v.id) AS episode_count,
                   COALESCE(SUM(CASE WHEN v.watched = 0 THEN 1 ELSE 0 END), 0) AS unwatched,
                   (SELECT file_path FROM videos vv WHERE vv.series_id = se.id
                    ORDER BY vv.episode_no LIMIT 1) AS thumb_seed
            FROM series se
            LEFT JOIN videos v ON v.series_id = se.id
            WHERE se.section_id = $sec
            GROUP BY se.id, se.section_id, se.base_title, se.is_standalone
            ORDER BY {orderBy}
            """;
        cmd.Parameters.AddWithValue("$sec", sectionId);
        var list = new List<SeriesSummary>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new SeriesSummary(
                r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetInt64(3) != 0,
                r.GetInt32(4), r.GetInt32(5), r.IsDBNull(6) ? null : r.GetString(6)));
        return list;
    }

    public IReadOnlyList<EpisodeView> GetEpisodes(long seriesId)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT v.id, v.series_id, v.file_path, v.episode_no, se.base_title, v.watched, v.missing
            FROM videos v
            JOIN series se ON se.id = v.series_id
            WHERE v.series_id = $s
            ORDER BY v.episode_no
            """;
        cmd.Parameters.AddWithValue("$s", seriesId);
        var list = new List<EpisodeView>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var episodeNo = r.GetInt32(3);
            var baseTitle = r.GetString(4);
            var title = episodeNo <= 1 ? baseTitle : $"{baseTitle} {episodeNo}";
            list.Add(new EpisodeView(
                r.GetInt64(0), r.GetInt64(1), r.GetString(2), episodeNo, title,
                r.GetInt64(5) != 0, r.GetInt64(6) != 0));
        }
        return list;
    }

    /// <summary>
    /// All playable (non-missing) episodes across every series in a section,
    /// in deterministic play order: series by sort_key, then episode_no.
    /// Used to build a "Play all" queue for a creator.
    /// </summary>
    public IReadOnlyList<EpisodeView> GetEpisodesForSection(long sectionId)
    {
        using var conn = db.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT v.id, v.series_id, v.file_path, v.episode_no, se.base_title, v.watched, v.missing
            FROM videos v
            JOIN series se ON se.id = v.series_id
            WHERE se.section_id = $sid AND v.missing = 0
            ORDER BY se.sort_key, v.episode_no;
            """;
        cmd.Parameters.AddWithValue("$sid", sectionId);
        var list = new List<EpisodeView>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var episodeNo = r.GetInt32(3);
            var baseTitle = r.GetString(4);
            var title = episodeNo <= 1 ? baseTitle : $"{baseTitle} {episodeNo}";
            list.Add(new EpisodeView(
                r.GetInt64(0), r.GetInt64(1), r.GetString(2), episodeNo, title,
                r.GetInt64(5) != 0, r.GetInt64(6) != 0));
        }
        return list;
    }

    /// <summary>Single episode by video id (for enqueue from Home cards that only carry a VideoId).</summary>
    public EpisodeView? GetEpisode(long videoId)
    {
        using var conn = db.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT v.id, v.series_id, v.file_path, v.episode_no, se.base_title, v.watched, v.missing
            FROM videos v
            JOIN series se ON se.id = v.series_id
            WHERE v.id = $vid;
            """;
        cmd.Parameters.AddWithValue("$vid", videoId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        var episodeNo = r.GetInt32(3);
        var baseTitle = r.GetString(4);
        var title = episodeNo <= 1 ? baseTitle : $"{baseTitle} {episodeNo}";
        return new EpisodeView(
            r.GetInt64(0), r.GetInt64(1), r.GetString(2), episodeNo, title,
            r.GetInt64(5) != 0, r.GetInt64(6) != 0);
    }

    /// <summary>Looks up an episode by its file path; returns null when not found.</summary>
    public EpisodeView? GetEpisodeByPath(string filePath)
    {
        using var conn = db.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT v.id, v.series_id, v.file_path, v.episode_no, se.base_title, v.watched, v.missing
            FROM videos v
            JOIN series se ON se.id = v.series_id
            WHERE v.file_path = $p;
            """;
        cmd.Parameters.AddWithValue("$p", filePath);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        var episodeNo = r.GetInt32(3);
        var baseTitle = r.GetString(4);
        var title = episodeNo <= 1 ? baseTitle : $"{baseTitle} {episodeNo}";
        return new EpisodeView(
            r.GetInt64(0), r.GetInt64(1), r.GetString(2), episodeNo, title,
            r.GetInt64(5) != 0, r.GetInt64(6) != 0);
    }

    public IReadOnlyList<SearchHit> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        // Escape LIKE wildcards in user input; match anywhere (contains).
        var escaped = query.Trim()
            .Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        var pattern = "%" + escaped + "%";

        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT 0 AS kind, sc.id AS target, sc.id AS section_id, sc.display_name AS title
            FROM sections sc WHERE sc.display_name LIKE $q ESCAPE '\'
            UNION ALL
            SELECT 1, se.id, se.section_id, se.base_title
            FROM series se WHERE se.base_title LIKE $q ESCAPE '\'
            UNION ALL
            SELECT 2, v.id, se.section_id, v.raw_filename
            FROM videos v JOIN series se ON se.id = v.series_id
            WHERE v.raw_filename LIKE $q ESCAPE '\'
            ORDER BY kind, title
            LIMIT 200
            """;
        cmd.Parameters.AddWithValue("$q", pattern);
        var list = new List<SearchHit>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new SearchHit(
                (SearchHitKind)r.GetInt32(0), r.GetInt64(1), r.GetInt64(2), r.GetString(3)));
        return list;
    }

    public IReadOnlyList<SectionSummary> SearchCreators(string query, int limit)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        var pattern = "%" + query.Trim()
            .Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";

        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT sc.id, sc.source_id, sc.display_name,
                   COUNT(DISTINCT se.id) AS series_count,
                   COALESCE(SUM(CASE WHEN v.id IS NOT NULL AND v.watched = 0 THEN 1 ELSE 0 END), 0) AS unwatched,
                   COUNT(v.id) AS video_count,
                   (SELECT v2.file_path
                      FROM videos v2
                      JOIN series se2 ON se2.id = v2.series_id
                     WHERE se2.section_id = sc.id AND v2.missing = 0
                     ORDER BY se2.id, v2.episode_no
                     LIMIT 1) AS seed_path
            FROM sections sc
            LEFT JOIN series se ON se.section_id = sc.id
            LEFT JOIN videos v ON v.series_id = se.id
            WHERE sc.display_name LIKE $q ESCAPE '\'
            GROUP BY sc.id, sc.source_id, sc.display_name
            ORDER BY sc.display_name
            LIMIT $limit
            """;
        cmd.Parameters.AddWithValue("$q", pattern);
        cmd.Parameters.AddWithValue("$limit", limit);
        var list = new List<SectionSummary>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new SectionSummary(
                SectionId: r.GetInt64(0), SourceId: r.GetInt64(1), DisplayName: r.GetString(2),
                SeriesCount: r.GetInt32(3), UnwatchedCount: r.GetInt32(4), VideoCount: r.GetInt32(5),
                ThumbnailSeedPath: r.IsDBNull(6) ? null : r.GetString(6)));
        return list;
    }

    public IReadOnlyList<RecencyItem> SearchVideos(string query, int limit)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        var pattern = "%" + query.Trim()
            .Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";

        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT v.id, v.series_id, s.section_id, s.base_title, s.is_standalone,
                   v.episode_no, v.watched, v.thumbnail_path
            FROM videos v
            JOIN series s ON s.id = v.series_id
            WHERE v.missing = 0
              AND (v.raw_filename LIKE $q ESCAPE '\' OR s.base_title LIKE $q ESCAPE '\')
            ORDER BY s.base_title, v.episode_no
            LIMIT $limit
            """;
        cmd.Parameters.AddWithValue("$q", pattern);
        cmd.Parameters.AddWithValue("$limit", limit);
        var list = new List<RecencyItem>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new RecencyItem(
                VideoId: r.GetInt64(0), SeriesId: r.GetInt64(1), SectionId: r.GetInt64(2),
                SeriesTitle: r.GetString(3), IsStandalone: r.GetInt64(4) != 0,
                EpisodeNo: r.GetInt32(5), Watched: r.GetInt64(6) != 0,
                ThumbnailSeedPath: r.IsDBNull(7) ? null : r.GetString(7)));
        return list;
    }

    public void RemoveSource(long sourceId)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        // ON DELETE CASCADE removes the source's sections/series/videos; foreign_keys=ON is set in Open().
        cmd.CommandText = "DELETE FROM sources WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", sourceId);
        cmd.ExecuteNonQuery();
    }

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
        cmd.CommandText = "UPDATE videos SET resume_position = $p, resume_updated_at = $t WHERE id = $id";
        cmd.Parameters.AddWithValue("$p", seconds);
        cmd.Parameters.AddWithValue("$t", System.DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$id", videoId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Clears the resume position (sets it NULL) — used when a video is marked watched or finishes.</summary>
    public void ClearResumePosition(long videoId)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE videos SET resume_position = NULL, resume_updated_at = NULL WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", videoId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Returns a random unwatched, non-missing episode across the whole library,
    /// or null if every video is watched or missing.</summary>
    public EpisodeView? GetRandomUnwatchedEpisode()
    {
        using var conn = db.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT v.id, v.series_id, v.file_path, v.episode_no, se.base_title, v.watched, v.missing
            FROM videos v
            JOIN series se ON se.id = v.series_id
            WHERE v.watched = 0 AND v.missing = 0
            ORDER BY RANDOM()
            LIMIT 1
            """;
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        var episodeNo = r.GetInt32(3);
        var baseTitle = r.GetString(4);
        var title = episodeNo <= 1 ? baseTitle : $"{baseTitle} {episodeNo}";
        return new EpisodeView(
            r.GetInt64(0), r.GetInt64(1), r.GetString(2), episodeNo, title,
            r.GetInt64(5) != 0, r.GetInt64(6) != 0);
    }

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

    /// <summary>Returns all present (non-missing) videos whose duration has not yet been probed.</summary>
    public IReadOnlyList<VideoToProbe> GetVideosNeedingDuration()
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, file_path FROM videos WHERE duration IS NULL AND missing = 0 ORDER BY id";
        var list = new List<VideoToProbe>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new VideoToProbe(r.GetInt64(0), r.GetString(1)));
        return list;
    }

    /// <summary>Saves the probed duration (seconds) for a video.</summary>
    public void SetDuration(long videoId, double seconds)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE videos SET duration = $d WHERE id = $id";
        cmd.Parameters.AddWithValue("$d", seconds);
        cmd.Parameters.AddWithValue("$id", videoId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Atomically replaces all chapters for a video. Idempotent on re-probe.</summary>
    public void ReplaceChapters(long videoId, IReadOnlyList<ChapterRecord> chapters)
    {
        using var conn = db.Open();
        using var tx = conn.BeginTransaction();

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM video_chapters WHERE video_id = $id";
            cmd.Parameters.AddWithValue("$id", videoId);
            cmd.ExecuteNonQuery();
        }

        foreach (var ch in chapters)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO video_chapters(video_id, idx, name, start_seconds)
                VALUES($id, $idx, $name, $start)
                """;
            cmd.Parameters.AddWithValue("$id", videoId);
            cmd.Parameters.AddWithValue("$idx", ch.Index);
            cmd.Parameters.AddWithValue("$name", ch.Name);
            cmd.Parameters.AddWithValue("$start", ch.StartSeconds);
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    /// <summary>Returns all chapters for a video, ordered by index.</summary>
    public IReadOnlyList<ChapterRecord> GetChapters(long videoId)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT idx, name, start_seconds FROM video_chapters WHERE video_id = $id ORDER BY idx";
        cmd.Parameters.AddWithValue("$id", videoId);
        var list = new List<ChapterRecord>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new ChapterRecord(r.GetInt32(0), r.GetString(1), r.GetDouble(2)));
        return list;
    }

    /// <summary>Repaths a video after an on-disk rename. Updates the stable row's file_path + raw_filename and
    /// any path-keyed grouping_overrides, in one transaction. Watched/resume/tags key off ids and are untouched.</summary>
    public void UpdateVideoPath(long videoId, string oldPath, string newPath)
    {
        using var conn = db.Open();
        using var tx = conn.BeginTransaction();

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE videos SET file_path = $new, raw_filename = $raw WHERE id = $id";
            cmd.Parameters.AddWithValue("$new", newPath);
            cmd.Parameters.AddWithValue("$raw", Path.GetFileName(newPath));
            cmd.Parameters.AddWithValue("$id", videoId);
            cmd.ExecuteNonQuery();
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE grouping_overrides SET file_path = $new WHERE file_path = $old";
            cmd.Parameters.AddWithValue("$new", newPath);
            cmd.Parameters.AddWithValue("$old", oldPath);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    // ── M18-A: size_bytes backfill ─────────────────────────────────────────────

    /// <summary>Returns ids+paths for present (non-missing) videos that have no <c>size_bytes</c> yet.</summary>
    public IReadOnlyList<VideoToProbe> GetVideosNeedingSize()
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, file_path FROM videos WHERE size_bytes IS NULL AND missing = 0 ORDER BY id";
        var list = new List<VideoToProbe>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new VideoToProbe(r.GetInt64(0), r.GetString(1)));
        return list;
    }

    /// <summary>Writes the file-system size in bytes for a single video.</summary>
    public void SetSizeBytes(long videoId, long bytes)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE videos SET size_bytes = $b WHERE id = $id";
        cmd.Parameters.AddWithValue("$b", bytes);
        cmd.Parameters.AddWithValue("$id", videoId);
        cmd.ExecuteNonQuery();
    }

    // ── M18-A: scan-diff helpers ───────────────────────────────────────────────

    /// <summary>
    /// Snapshot of path → wasMissing for all videos under a source, taken BEFORE
    /// <see cref="MarkAllMissingForSource"/> is called. Used by <c>ScanService</c> to
    /// classify each re-found file as Added / Restored / Updated.
    /// </summary>
    public Dictionary<string, bool> GetVideoPathStates(long sourceId)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT v.file_path, v.missing
            FROM videos v
            JOIN series se ON se.id = v.series_id
            JOIN sections sc ON sc.id = se.section_id
            WHERE sc.source_id = $src
            """;
        cmd.Parameters.AddWithValue("$src", sourceId);
        var dict = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            dict[r.GetString(0)] = r.GetInt64(1) != 0;
        return dict;
    }

    /// <summary>Count of videos still marked missing under a source after the scan walk.</summary>
    public int CountMissingForSource(long sourceId)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM videos v
            JOIN series se ON se.id = v.series_id
            JOIN sections sc ON sc.id = se.section_id
            WHERE sc.source_id = $src AND v.missing = 1
            """;
        cmd.Parameters.AddWithValue("$src", sourceId);
        return (int)(long)cmd.ExecuteScalar()!;
    }

    /// <summary>Writes the ISO8601 last-scan timestamp for a source (rounds to nearest second).</summary>
    public void SetSourceLastScanUtc(long sourceId, DateTimeOffset utc)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE sources SET last_scan_utc = $t WHERE id = $id";
        cmd.Parameters.AddWithValue("$t", utc.ToString("o"));
        cmd.Parameters.AddWithValue("$id", sourceId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Returns the last-scan UTC timestamp for a source, or null if never scanned.</summary>
    public DateTimeOffset? GetSourceLastScanUtc(long sourceId)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT last_scan_utc FROM sources WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", sourceId);
        var result = cmd.ExecuteScalar();
        if (result is null or System.DBNull) return null;
        return DateTimeOffset.Parse((string)result, null, DateTimeStyles.RoundtripKind);
    }

    // ── M18-B: grouping override CRUD ─────────────────────────────────────────

    /// <summary>
    /// Returns all grouping overrides for a section, keyed by <b>bare file name</b>
    /// (<c>Path.GetFileName(file_path)</c>) so <see cref="SectionGrouper.Group"/> can look
    /// them up without knowing the section's root path.
    /// </summary>
    public IReadOnlyDictionary<string, GroupingOverride> GetGroupingOverrides(long sectionId)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT file_path, override_base_title, override_episode_no
            FROM grouping_overrides
            WHERE section_id = @sec
            """;
        cmd.Parameters.AddWithValue("@sec", sectionId);
        var dict = new Dictionary<string, GroupingOverride>(StringComparer.OrdinalIgnoreCase);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var filePath = r.GetString(0);
            var baseTitle = r.IsDBNull(1) ? null : r.GetString(1);
            var episodeNo = r.IsDBNull(2) ? (int?)null : (int)r.GetInt64(2);
            var bareFileName = Path.GetFileName(filePath);
            dict[bareFileName] = new GroupingOverride(filePath, baseTitle, episodeNo);
        }
        return dict;
    }

    /// <summary>
    /// Upserts a grouping override for a single file within a section.
    /// Uses <c>INSERT … ON CONFLICT(section_id, file_path) DO UPDATE</c> (@-prefixed params).
    /// Setting both <paramref name="baseTitle"/> and <paramref name="episodeNo"/> to null
    /// is semantically equivalent to <see cref="ClearGroupingOverride"/> (the row is kept
    /// but applies no change during grouping); callers that want to remove the row should use
    /// <see cref="ClearGroupingOverride"/> instead.
    /// </summary>
    public void SetGroupingOverride(long sectionId, string filePath, string? baseTitle, int? episodeNo)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO grouping_overrides(section_id, file_path, override_base_title, override_episode_no)
            VALUES(@sec, @path, @title, @epno)
            ON CONFLICT(section_id, file_path) DO UPDATE
                SET override_base_title = excluded.override_base_title,
                    override_episode_no  = excluded.override_episode_no
            """;
        cmd.Parameters.AddWithValue("@sec",   sectionId);
        cmd.Parameters.AddWithValue("@path",  filePath);
        cmd.Parameters.AddWithValue("@title", (object?)baseTitle ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@epno",  (object?)episodeNo ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Removes a grouping override for a single file, restoring parser-derived grouping for it.</summary>
    public void ClearGroupingOverride(long sectionId, string filePath)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            DELETE FROM grouping_overrides
            WHERE section_id = @sec AND file_path = @path
            """;
        cmd.Parameters.AddWithValue("@sec",  sectionId);
        cmd.Parameters.AddWithValue("@path", filePath);
        cmd.ExecuteNonQuery();
    }

    // ── M18-C: resolution backfill ────────────────────────────────────────────

    /// <summary>Returns ids+paths for present (non-missing) videos that have no <c>width</c> yet.</summary>
    public IReadOnlyList<VideoToProbe> GetVideosNeedingResolution()
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, file_path FROM videos WHERE width IS NULL AND missing = 0 ORDER BY id";
        var list = new List<VideoToProbe>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new VideoToProbe(r.GetInt64(0), r.GetString(1)));
        return list;
    }

    /// <summary>Writes the probed pixel dimensions for a single video.</summary>
    public void SetResolution(long videoId, int width, int height)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE videos SET width = $w, height = $h WHERE id = $id";
        cmd.Parameters.AddWithValue("$w", width);
        cmd.Parameters.AddWithValue("$h", height);
        cmd.Parameters.AddWithValue("$id", videoId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Forces <c>missing = 1</c> on a single video by id.
    /// Harness/seed use only — production code never calls this directly; the scan pipeline
    /// uses <see cref="MarkAllMissingForSource"/> + <see cref="ClearMissing"/> instead.
    /// </summary>
    public void SetVideoMissing(long videoId)
    {
        using var conn = db.Open();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = "UPDATE videos SET missing = 1 WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", videoId);
        cmd.ExecuteNonQuery();
    }

    // ── M18-F: relink helpers ──────────────────────────────────────────────────

    /// <summary>
    /// Returns the source root_path for the source that owns the given video,
    /// or null if the video or its source is not found. Used by auto-find relink
    /// to scope the directory walk to the right source folder.
    /// </summary>
    public string? GetSourceRootForVideo(long videoId)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT sr.root_path
            FROM videos v
            JOIN series se ON se.id = v.series_id
            JOIN sections sc ON sc.id = se.section_id
            JOIN sources sr ON sr.id = sc.source_id
            WHERE v.id = $id
            """;
        cmd.Parameters.AddWithValue("$id", videoId);
        var result = cmd.ExecuteScalar();
        return result is string s ? s : null;
    }

    /// <summary>
    /// Repaths a video (relink after manual move) and clears its missing flag in one transaction.
    /// Watched-state/tags/chapters survive because they key off the stable video id.
    /// Also updates any path-keyed grouping_overrides for the old path.
    /// </summary>
    public void RelinkVideo(long videoId, string oldPath, string newPath)
    {
        using var conn = db.Open();
        using var tx = conn.BeginTransaction();

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE videos SET file_path = $new, raw_filename = $raw, missing = 0 WHERE id = $id";
            cmd.Parameters.AddWithValue("$new", newPath);
            cmd.Parameters.AddWithValue("$raw", System.IO.Path.GetFileName(newPath));
            cmd.Parameters.AddWithValue("$id", videoId);
            cmd.ExecuteNonQuery();
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE grouping_overrides SET file_path = $new WHERE file_path = $old";
            cmd.Parameters.AddWithValue("$new", newPath);
            cmd.Parameters.AddWithValue("$old", oldPath);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>
    /// Removes a single video row from the DB index.
    /// CASCADE FK constraints on the schema clean up <c>video_tags</c>,
    /// <c>video_chapters</c>, <c>watch_events</c>, and <c>video_art</c> automatically.
    /// Does NOT touch the filesystem — call <see cref="IRecycleBinService"/> before this.
    /// </summary>
    public void DeleteVideoIndexById(long videoId)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM videos WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", videoId);
        cmd.ExecuteNonQuery();
    }

    // ── M18-H: in-memory regroup (no disk scan) ───────────────────────────────

    /// <summary>
    /// Re-buckets all videos in a section using the override-aware
    /// <see cref="Naming.SectionGrouper.Group"/> overload — no filesystem access.
    /// Re-runs grouping on the current <c>videos.file_path</c> rows, then
    /// upserts the resulting <c>series_id</c> and <c>episode_no</c> back in
    /// one transaction. Watched-state/tags/chapters are preserved because they
    /// key off stable video ids; only <c>videos.series_id</c> and
    /// <c>videos.episode_no</c> are updated.
    ///
    /// <para>Idempotent: running twice produces the same result as running once.
    /// A subsequent full disk scan calls the same grouper logic and produces
    /// the same assignment, so RegroupSection and ScanSource are consistent.</para>
    /// </summary>
    public void RegroupSection(long sectionId)
    {
        // 1. Collect all (video_id, file_path) rows for this section.
        var videoRows = new List<(long Id, string FilePath)>();
        using (var conn = db.Open())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT v.id, v.file_path
                FROM videos v
                JOIN series se ON se.id = v.series_id
                WHERE se.section_id = @sec
                """;
            cmd.Parameters.AddWithValue("@sec", sectionId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                videoRows.Add((r.GetInt64(0), r.GetString(1)));
        }
        if (videoRows.Count == 0) return;

        // 2. Re-run grouping in memory (SectionGrouper takes bare file names).
        var overrides = GetGroupingOverrides(sectionId);
        var fileNames = videoRows.Select(v => Path.GetFileName(v.FilePath)).ToList();
        var grouped   = Naming.SectionGrouper.Group(fileNames, overrides);

        // 3. Build a map: bare_filename -> (new base_title, is_standalone, episode_no)
        var plan = new Dictionary<string, (string BaseTitle, bool IsStandalone, int EpisodeNo)>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var gs in grouped.Series)
            foreach (var ge in gs.Episodes)
                plan[ge.FileName] = (gs.BaseTitle, gs.IsStandalone, ge.EpisodeNumber);

        // 4. Apply in a single transaction: upsert series rows, then update each video.
        using var txConn = db.Open();
        using var tx    = txConn.BeginTransaction();

        using (var cmd = txConn.CreateCommand())
        {
            cmd.Transaction = tx;
            foreach (var row in videoRows)
            {
                var bare = Path.GetFileName(row.FilePath);
                if (!plan.TryGetValue(bare, out var p)) continue;

                // Ensure the target series row exists (idempotent upsert).
                cmd.CommandText = """
                    INSERT INTO series(section_id, base_title, sort_key, is_standalone)
                    VALUES(@sec, @bt, @sk, @sa)
                    ON CONFLICT(section_id, base_title) DO UPDATE SET is_standalone=excluded.is_standalone
                    RETURNING id
                    """;
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("@sec", sectionId);
                cmd.Parameters.AddWithValue("@bt", p.BaseTitle);
                cmd.Parameters.AddWithValue("@sk", p.BaseTitle.ToLowerInvariant());
                cmd.Parameters.AddWithValue("@sa", p.IsStandalone ? 1 : 0);
                var newSeriesId = (long)cmd.ExecuteScalar()!;

                cmd.CommandText = "UPDATE videos SET series_id = @sid, episode_no = @ep WHERE id = @vid";
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("@sid", newSeriesId);
                cmd.Parameters.AddWithValue("@ep",  p.EpisodeNo);
                cmd.Parameters.AddWithValue("@vid", row.Id);
                cmd.ExecuteNonQuery();
            }
        }
        tx.Commit();
    }
}
