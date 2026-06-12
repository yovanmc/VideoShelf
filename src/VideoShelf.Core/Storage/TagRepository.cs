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
}
