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
}
