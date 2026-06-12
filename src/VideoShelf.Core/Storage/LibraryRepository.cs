using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using VideoShelf.Core.Models;

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

    public long UpsertVideo(long seriesId, string filePath, int episodeNo, string format)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO videos(series_id, file_path, episode_no, raw_filename, format, added_at, missing)
            VALUES($s, $p, $e, $r, $f, $at, 0)
            ON CONFLICT(file_path) DO UPDATE SET series_id=excluded.series_id,
                episode_no=excluded.episode_no, raw_filename=excluded.raw_filename,
                format=excluded.format
            RETURNING id;
            """;
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
                   thumbnail_path, watched, added_at, missing
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
                r.IsDBNull(9) ? "" : r.GetString(9), r.GetInt64(10) != 0));
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
                   COALESCE(SUM(CASE WHEN v.id IS NOT NULL AND v.watched = 0 THEN 1 ELSE 0 END), 0) AS unwatched
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
                r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetInt32(3), r.GetInt32(4)));
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
}
