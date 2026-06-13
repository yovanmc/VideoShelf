using System;
using System.Collections.Generic;
using System.Text.Json;
using VideoShelf.Core.Discovery;

namespace VideoShelf.Core.Storage;

/// <summary>A persisted smart view, combining metadata with its filter definition.</summary>
public sealed record SmartView(long Id, string Name, SmartViewDefinition Definition, int SortOrder, bool ShowOnHome, string CreatedAt);

/// <summary>CRUD + query operations for user-defined smart views.</summary>
public sealed class SmartViewRepository(VideoShelfDb db)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    // ── Read ─────────────────────────────────────────────────────────────────

    public IReadOnlyList<SmartView> GetAll()
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, definition, sort_order, show_on_home, created_at
            FROM smart_views
            ORDER BY sort_order, id;
            """;
        return ReadRows(cmd);
    }

    public IReadOnlyList<SmartView> GetHomeViews()
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, definition, sort_order, show_on_home, created_at
            FROM smart_views
            WHERE show_on_home = 1
            ORDER BY sort_order, id;
            """;
        return ReadRows(cmd);
    }

    // ── Write ────────────────────────────────────────────────────────────────

    public long Create(string name, SmartViewDefinition def, bool showOnHome, DateTimeOffset now)
    {
        var json = JsonSerializer.Serialize(def, JsonOpts);
        var createdAt = now.ToString("o");
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO smart_views(name, definition, sort_order, show_on_home, created_at)
            VALUES($name, $definition, 0, $showOnHome, $createdAt);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$definition", json);
        cmd.Parameters.AddWithValue("$showOnHome", showOnHome ? 1 : 0);
        cmd.Parameters.AddWithValue("$createdAt", createdAt);
        return (long)cmd.ExecuteScalar()!;
    }

    public void Update(long id, string name, SmartViewDefinition def, bool showOnHome)
    {
        var json = JsonSerializer.Serialize(def, JsonOpts);
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE smart_views
            SET name = $name, definition = $definition, show_on_home = $showOnHome
            WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$definition", json);
        cmd.Parameters.AddWithValue("$showOnHome", showOnHome ? 1 : 0);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void Delete(long id)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM smart_views WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void Reorder(long id, int sortOrder)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE smart_views SET sort_order = $sortOrder WHERE id = $id;";
        cmd.Parameters.AddWithValue("$sortOrder", sortOrder);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    // ── Query ────────────────────────────────────────────────────────────────

    public IReadOnlyList<RecencyItem> GetMatchingVideos(SmartViewDefinition def, int limit, DateTimeOffset now)
    {
        var (where, builderParams) = SmartViewSqlBuilder.Build(def, now);

        var whereClause = string.IsNullOrEmpty(where)
            ? "v.missing = 0"
            : $"v.missing = 0 AND {where}";

        var sql = $"""
            SELECT v.id, v.series_id, s.section_id, s.base_title, s.is_standalone,
                   v.episode_no, v.watched, v.thumbnail_path
            FROM videos v JOIN series s ON s.id = v.series_id
            WHERE {whereClause}
            ORDER BY v.added_at DESC, v.id DESC
            LIMIT $limit;
            """;

        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$limit", limit);
        foreach (var p in builderParams)
            cmd.Parameters.AddWithValue(p.Name, p.Value);

        var result = new List<RecencyItem>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            result.Add(new RecencyItem(
                VideoId: r.GetInt64(0),
                SeriesId: r.GetInt64(1),
                SectionId: r.GetInt64(2),
                SeriesTitle: r.GetString(3),
                IsStandalone: r.GetInt64(4) != 0,
                EpisodeNo: r.GetInt32(5),
                Watched: r.GetInt64(6) != 0,
                ThumbnailSeedPath: r.IsDBNull(7) ? null : r.GetString(7)));
        }
        return result;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static IReadOnlyList<SmartView> ReadRows(Microsoft.Data.Sqlite.SqliteCommand cmd)
    {
        var result = new List<SmartView>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var def = JsonSerializer.Deserialize<SmartViewDefinition>(r.GetString(2), JsonOpts)!;
            result.Add(new SmartView(
                Id: r.GetInt64(0),
                Name: r.GetString(1),
                Definition: def,
                SortOrder: r.GetInt32(3),
                ShowOnHome: r.GetInt64(4) != 0,
                CreatedAt: r.GetString(5)));
        }
        return result;
    }
}
