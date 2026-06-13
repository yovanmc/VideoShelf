using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace VideoShelf.Core.Storage;

public sealed record TagCount(string Tag, int SectionCount);

public sealed class TagRepository(VideoShelfDb db)
{
    public static string Normalize(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return string.Empty;
        var parts = tag.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts).ToLowerInvariant();
    }

    public IReadOnlyList<string> GetTags(long sectionId)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT tag FROM section_tags WHERE section_id = @s ORDER BY tag;";
        cmd.Parameters.AddWithValue("@s", sectionId);
        var result = new List<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) result.Add(r.GetString(0));
        return result;
    }

    public void AddTag(long sectionId, string tag)
    {
        var norm = Normalize(tag);
        if (norm.Length == 0) return;
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO section_tags (section_id, tag) VALUES (@s, @t);";
        cmd.Parameters.AddWithValue("@s", sectionId);
        cmd.Parameters.AddWithValue("@t", norm);
        cmd.ExecuteNonQuery();
    }

    public void RemoveTag(long sectionId, string tag)
    {
        var norm = Normalize(tag);
        if (norm.Length == 0) return;
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM section_tags WHERE section_id = @s AND tag = @t;";
        cmd.Parameters.AddWithValue("@s", sectionId);
        cmd.Parameters.AddWithValue("@t", norm);
        cmd.ExecuteNonQuery();
    }

    public void SetTags(long sectionId, IEnumerable<string> tags)
    {
        var normalized = tags.Select(Normalize).Where(t => t.Length > 0).Distinct().ToList();
        using var conn = db.Open();
        using var tx = conn.BeginTransaction();
        using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM section_tags WHERE section_id = @s;";
            del.Parameters.AddWithValue("@s", sectionId);
            del.ExecuteNonQuery();
        }
        foreach (var t in normalized)
        {
            using var ins = conn.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = "INSERT OR IGNORE INTO section_tags (section_id, tag) VALUES (@s, @t);";
            ins.Parameters.AddWithValue("@s", sectionId);
            ins.Parameters.AddWithValue("@t", t);
            ins.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public IReadOnlyList<string> GetAllTags()
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT tag FROM section_tags ORDER BY tag;";
        var result = new List<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) result.Add(r.GetString(0));
        return result;
    }

    public IReadOnlyList<TagCount> GetTagCounts()
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT tag, COUNT(DISTINCT section_id) FROM section_tags GROUP BY tag ORDER BY tag;";
        var result = new List<TagCount>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) result.Add(new TagCount(r.GetString(0), r.GetInt32(1)));
        return result;
    }

    // ── series-level ────────────────────────────────────────────────────────

    public IReadOnlyList<string> GetSeriesTags(long seriesId)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT tag FROM series_tags WHERE series_id = @s ORDER BY tag;";
        cmd.Parameters.AddWithValue("@s", seriesId);
        var result = new List<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) result.Add(r.GetString(0));
        return result;
    }

    public void AddSeriesTag(long seriesId, string tag)
    {
        var norm = Normalize(tag);
        if (norm.Length == 0) return;
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO series_tags (series_id, tag) VALUES (@s, @t);";
        cmd.Parameters.AddWithValue("@s", seriesId);
        cmd.Parameters.AddWithValue("@t", norm);
        cmd.ExecuteNonQuery();
    }

    public void RemoveSeriesTag(long seriesId, string tag)
    {
        var norm = Normalize(tag);
        if (norm.Length == 0) return;
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM series_tags WHERE series_id = @s AND tag = @t;";
        cmd.Parameters.AddWithValue("@s", seriesId);
        cmd.Parameters.AddWithValue("@t", norm);
        cmd.ExecuteNonQuery();
    }

    public void SetSeriesTags(long seriesId, IEnumerable<string> tags)
    {
        var normalized = tags.Select(Normalize).Where(t => t.Length > 0).Distinct().ToList();
        using var conn = db.Open();
        using var tx = conn.BeginTransaction();
        using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM series_tags WHERE series_id = @s;";
            del.Parameters.AddWithValue("@s", seriesId);
            del.ExecuteNonQuery();
        }
        foreach (var t in normalized)
        {
            using var ins = conn.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = "INSERT OR IGNORE INTO series_tags (series_id, tag) VALUES (@s, @t);";
            ins.Parameters.AddWithValue("@s", seriesId);
            ins.Parameters.AddWithValue("@t", t);
            ins.ExecuteNonQuery();
        }
        tx.Commit();
    }

    // ── video-level ─────────────────────────────────────────────────────────

    public IReadOnlyList<string> GetVideoTags(long videoId)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT tag FROM video_tags WHERE video_id = @v ORDER BY tag;";
        cmd.Parameters.AddWithValue("@v", videoId);
        var result = new List<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) result.Add(r.GetString(0));
        return result;
    }

    public void AddVideoTag(long videoId, string tag)
    {
        var norm = Normalize(tag);
        if (norm.Length == 0) return;
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO video_tags (video_id, tag) VALUES (@v, @t);";
        cmd.Parameters.AddWithValue("@v", videoId);
        cmd.Parameters.AddWithValue("@t", norm);
        cmd.ExecuteNonQuery();
    }

    public void RemoveVideoTag(long videoId, string tag)
    {
        var norm = Normalize(tag);
        if (norm.Length == 0) return;
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM video_tags WHERE video_id = @v AND tag = @t;";
        cmd.Parameters.AddWithValue("@v", videoId);
        cmd.Parameters.AddWithValue("@t", norm);
        cmd.ExecuteNonQuery();
    }

    public void SetVideoTags(long videoId, IEnumerable<string> tags)
    {
        var normalized = tags.Select(Normalize).Where(t => t.Length > 0).Distinct().ToList();
        using var conn = db.Open();
        using var tx = conn.BeginTransaction();
        using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM video_tags WHERE video_id = @v;";
            del.Parameters.AddWithValue("@v", videoId);
            del.ExecuteNonQuery();
        }
        foreach (var t in normalized)
        {
            using var ins = conn.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = "INSERT OR IGNORE INTO video_tags (video_id, tag) VALUES (@v, @t);";
            ins.Parameters.AddWithValue("@v", videoId);
            ins.Parameters.AddWithValue("@t", t);
            ins.ExecuteNonQuery();
        }
        tx.Commit();
    }

    // ── parent-tag resolution helpers ───────────────────────────────────────

    /// <summary>Returns the section_tags of the parent section of the given series.</summary>
    public IReadOnlyList<string> GetSectionTagsForSeries(long seriesId)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT st.tag FROM section_tags st
            JOIN series s ON s.section_id = st.section_id
            WHERE s.id = @id ORDER BY st.tag;
            """;
        cmd.Parameters.AddWithValue("@id", seriesId);
        var result = new List<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) result.Add(r.GetString(0));
        return result;
    }

    /// <summary>Returns the series_tags of the parent series of the given video.</summary>
    public IReadOnlyList<string> GetSeriesTagsForVideo(long videoId)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT st.tag FROM series_tags st
            JOIN videos v ON v.series_id = st.series_id
            WHERE v.id = @id ORDER BY st.tag;
            """;
        cmd.Parameters.AddWithValue("@id", videoId);
        var result = new List<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) result.Add(r.GetString(0));
        return result;
    }

    /// <summary>Returns the section_tags of the grandparent section of the given video.</summary>
    public IReadOnlyList<string> GetSectionTagsForVideo(long videoId)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT sect.tag FROM section_tags sect
            JOIN series s ON s.section_id = sect.section_id
            JOIN videos v ON v.series_id = s.id
            WHERE v.id = @id ORDER BY sect.tag;
            """;
        cmd.Parameters.AddWithValue("@id", videoId);
        var result = new List<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) result.Add(r.GetString(0));
        return result;
    }

    // ── resolution + universe ────────────────────────────────────────────────

    public IReadOnlyList<string> GetEffectiveVideoTags(long videoId)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT DISTINCT tag FROM (
                SELECT tag FROM video_tags WHERE video_id = @v
                UNION
                SELECT st.tag FROM series_tags st JOIN videos v ON v.series_id = st.series_id WHERE v.id = @v
                UNION
                SELECT sect.tag FROM section_tags sect
                    JOIN series s ON s.section_id = sect.section_id
                    JOIN videos v ON v.series_id = s.id WHERE v.id = @v
            ) ORDER BY tag;
            """;
        cmd.Parameters.AddWithValue("@v", videoId);
        var result = new List<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) result.Add(r.GetString(0));
        return result;
    }

    public IReadOnlyList<string> GetAllTagsAcrossLevels()
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT DISTINCT tag FROM (
                SELECT tag FROM section_tags
                UNION
                SELECT tag FROM series_tags
                UNION
                SELECT tag FROM video_tags
            ) ORDER BY tag;
            """;
        var result = new List<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) result.Add(r.GetString(0));
        return result;
    }
}
