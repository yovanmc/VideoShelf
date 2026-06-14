using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;
using VideoShelf.Core.Models;

namespace VideoShelf.Core.Storage;

/// <summary>
/// Read queries and dismissal writes for the M18 maintenance dashboard.
/// Covers D1 (duplicate detection), D2 (dismissals), D3 (summary), D4 (lists + index deletes).
/// </summary>
public sealed class MaintenanceRepository(VideoShelfDb db)
{
    // ── D1: Duplicate groups ──────────────────────────────────────────────────

    /// <summary>
    /// Returns all duplicate candidate groups across the whole library.
    /// Two videos are candidates iff both are present (missing=0), have equal size_bytes,
    /// and equal CAST(ROUND(duration) AS INTEGER). Dismissal pairs are excluded in C#.
    /// </summary>
    public IReadOnlyList<DuplicateGroup> GetDuplicateGroups()
        => BuildGroups(GetRawDuplicateRows(sectionId: null), GetDismissedPairs());

    /// <summary>
    /// Returns duplicate candidate groups scoped to a single creator section.
    /// </summary>
    public IReadOnlyList<DuplicateGroup> GetDuplicateGroupsForSection(long sectionId)
        => BuildGroups(GetRawDuplicateRows(sectionId: sectionId), GetDismissedPairs());

    private IReadOnlyList<DuplicateVideo> GetRawDuplicateRows(long? sectionId)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();

        if (sectionId is null)
        {
            cmd.CommandText = """
                SELECT v.id, sc.id, sc.display_name, se.base_title,
                       v.file_path, v.size_bytes, v.duration, v.width, v.height
                FROM videos v
                JOIN series se ON se.id = v.series_id
                JOIN sections sc ON sc.id = se.section_id
                WHERE v.missing = 0
                  AND v.size_bytes IS NOT NULL
                  AND v.duration IS NOT NULL
                  AND EXISTS (
                      SELECT 1 FROM videos v2
                      WHERE v2.missing = 0
                        AND v2.size_bytes IS NOT NULL
                        AND v2.duration IS NOT NULL
                        AND v2.size_bytes = v.size_bytes
                        AND CAST(ROUND(v2.duration) AS INTEGER) = CAST(ROUND(v.duration) AS INTEGER)
                        AND v2.id <> v.id
                  )
                ORDER BY v.size_bytes, CAST(ROUND(v.duration) AS INTEGER), v.id
                """;
        }
        else
        {
            cmd.CommandText = """
                SELECT v.id, sc.id, sc.display_name, se.base_title,
                       v.file_path, v.size_bytes, v.duration, v.width, v.height
                FROM videos v
                JOIN series se ON se.id = v.series_id
                JOIN sections sc ON sc.id = se.section_id
                WHERE v.missing = 0
                  AND v.size_bytes IS NOT NULL
                  AND v.duration IS NOT NULL
                  AND sc.id = @sec
                  AND EXISTS (
                      SELECT 1 FROM videos v2
                      JOIN series se2 ON se2.id = v2.series_id
                      WHERE v2.missing = 0
                        AND v2.size_bytes IS NOT NULL
                        AND v2.duration IS NOT NULL
                        AND se2.section_id = @sec
                        AND v2.size_bytes = v.size_bytes
                        AND CAST(ROUND(v2.duration) AS INTEGER) = CAST(ROUND(v.duration) AS INTEGER)
                        AND v2.id <> v.id
                  )
                ORDER BY v.size_bytes, CAST(ROUND(v.duration) AS INTEGER), v.id
                """;
            cmd.Parameters.AddWithValue("@sec", sectionId.Value);
        }

        var list = new List<DuplicateVideo>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new DuplicateVideo(
                Id: r.GetInt64(0),
                SectionId: r.GetInt64(1),
                CreatorName: r.GetString(2),
                SeriesTitle: r.GetString(3),
                FilePath: r.GetString(4),
                SizeBytes: r.IsDBNull(5) ? null : r.GetInt64(5),
                DurationSeconds: r.IsDBNull(6) ? null : r.GetDouble(6),
                Width: r.IsDBNull(7) ? null : (int?)r.GetInt32(7),
                Height: r.IsDBNull(8) ? null : (int?)r.GetInt32(8)));
        }
        return list;
    }

    /// <summary>
    /// Applies dismissal filtering to raw rows: a video is dropped from a group if it has
    /// been dismissed against every other member. Groups with fewer than 2 remaining members
    /// are removed entirely.
    /// </summary>
    internal static IReadOnlyList<DuplicateGroup> BuildGroups(
        IReadOnlyList<DuplicateVideo> rows,
        IReadOnlyList<(long A, long B)> dismissed)
    {
        var dismissedSet = new HashSet<(long, long)>(dismissed);

        // Group by (size_bytes, rounded_duration_seconds)
        var grouped = rows
            .GroupBy(v => (SizeBytes: v.SizeBytes!.Value, Duration: (int)Math.Round(v.DurationSeconds!.Value)))
            .ToList();

        var result = new List<DuplicateGroup>();
        foreach (var g in grouped)
        {
            var candidates = g.ToList();
            // Filter: keep a video only if at least one other member in the group is NOT dismissed against it
            var kept = candidates
                .Where(v => candidates.Any(other =>
                    other.Id != v.Id && !IsDismissed(v.Id, other.Id, dismissedSet)))
                .ToList();

            if (kept.Count >= 2)
                result.Add(new DuplicateGroup(g.Key.SizeBytes, g.Key.Duration, kept));
        }
        return result;
    }

    private static bool IsDismissed(long idA, long idB, HashSet<(long, long)> set)
    {
        var key = idA < idB ? (idA, idB) : (idB, idA);
        return set.Contains(key);
    }

    // ── D2: Dismissals ────────────────────────────────────────────────────────

    /// <summary>Records that the owner has reviewed and dismissed a duplicate pair (stored ordered min/max).</summary>
    public void DismissDuplicatePair(long videoIdA, long videoIdB, DateTimeOffset now)
    {
        var (lo, hi) = videoIdA < videoIdB ? (videoIdA, videoIdB) : (videoIdB, videoIdA);
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO dismissed_duplicates(video_id_a, video_id_b, dismissed_at)
            VALUES(@a, @b, @t)
            """;
        cmd.Parameters.AddWithValue("@a", lo);
        cmd.Parameters.AddWithValue("@b", hi);
        cmd.Parameters.AddWithValue("@t", now.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    /// <summary>Returns true if this pair has already been dismissed (order-independent).</summary>
    public bool IsDuplicatePairDismissed(long videoIdA, long videoIdB)
    {
        var (lo, hi) = videoIdA < videoIdB ? (videoIdA, videoIdB) : (videoIdB, videoIdA);
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM dismissed_duplicates
            WHERE video_id_a = @a AND video_id_b = @b
            """;
        cmd.Parameters.AddWithValue("@a", lo);
        cmd.Parameters.AddWithValue("@b", hi);
        return (long)cmd.ExecuteScalar()! > 0;
    }

    /// <summary>Returns all dismissed pairs as ordered (A, B) tuples (A &lt; B).</summary>
    public IReadOnlyList<(long A, long B)> GetDismissedPairs()
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT video_id_a, video_id_b FROM dismissed_duplicates ORDER BY video_id_a, video_id_b";
        var list = new List<(long, long)>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add((r.GetInt64(0), r.GetInt64(1)));
        return list;
    }

    // ── D3: Maintenance summary ───────────────────────────────────────────────

    /// <summary>
    /// Aggregates health counts and DB size for the dashboard header tiles.
    /// DB size = PRAGMA page_count * PRAGMA page_size.
    /// </summary>
    public MaintenanceSummary GetMaintenanceSummary()
    {
        using var conn = db.Open();

        int missing = ScalarInt(conn, "SELECT COUNT(*) FROM videos WHERE missing = 1");

        int orphanSeries = ScalarInt(conn, """
            SELECT COUNT(*) FROM series se
            WHERE NOT EXISTS (
                SELECT 1 FROM videos v WHERE v.series_id = se.id AND v.missing = 0
            )
            """);

        int emptyCreators = ScalarInt(conn, """
            SELECT COUNT(*) FROM sections sc
            WHERE NOT EXISTS (
                SELECT 1 FROM videos v
                JOIN series se ON se.id = v.series_id
                WHERE se.section_id = sc.id AND v.missing = 0
            )
            """);

        int singleFileSeries = ScalarInt(conn, """
            SELECT COUNT(*) FROM (
                SELECT se.id FROM series se
                JOIN videos v ON v.series_id = se.id AND v.missing = 0
                GROUP BY se.id
                HAVING COUNT(*) = 1
            )
            """);

        long pageCount = ScalarLong(conn, "PRAGMA page_count");
        long pageSize  = ScalarLong(conn, "PRAGMA page_size");
        long dbSize    = pageCount * pageSize;

        // Duplicate group count from D1 logic (library-wide)
        var rawRows  = GetRawDuplicateRows(sectionId: null);
        var pairs    = GetDismissedPairsViaConn(conn);
        var groups   = BuildGroups(rawRows, pairs);
        int dupGroups = groups.Count;

        return new MaintenanceSummary(missing, orphanSeries, emptyCreators, singleFileSeries, dupGroups, dbSize);
    }

    private IReadOnlyList<(long A, long B)> GetDismissedPairsViaConn(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT video_id_a, video_id_b FROM dismissed_duplicates ORDER BY video_id_a, video_id_b";
        var list = new List<(long, long)>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add((r.GetInt64(0), r.GetInt64(1)));
        return list;
    }

    private static int ScalarInt(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var v = cmd.ExecuteScalar();
        return v is long l ? (int)l : Convert.ToInt32(v);
    }

    private static long ScalarLong(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var v = cmd.ExecuteScalar();
        return v is long l ? l : Convert.ToInt64(v);
    }

    // ── D4: Missing/orphan lists + index deletes ──────────────────────────────

    /// <summary>All videos marked missing, with creator and series context for the relink triage list.
    /// Includes size_bytes and duration so the auto-find matcher can compare candidates.</summary>
    public IReadOnlyList<MissingVideo> GetMissingVideos()
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT v.id, v.file_path, sc.display_name, se.base_title, v.size_bytes, v.duration
            FROM videos v
            JOIN series se ON se.id = v.series_id
            JOIN sections sc ON sc.id = se.section_id
            WHERE v.missing = 1
            ORDER BY sc.display_name, se.base_title, v.file_path
            """;
        var list = new List<MissingVideo>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new MissingVideo(
                r.GetInt64(0),
                r.GetString(1),
                r.GetString(2),
                r.GetString(3),
                r.IsDBNull(4) ? null : r.GetInt64(4),
                r.IsDBNull(5) ? null : r.GetDouble(5)));
        return list;
    }

    /// <summary>Series that have zero playable (non-missing) videos.</summary>
    public IReadOnlyList<OrphanEntry> GetOrphanSeries()
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT se.id, se.base_title, sc.display_name
            FROM series se
            JOIN sections sc ON sc.id = se.section_id
            WHERE NOT EXISTS (
                SELECT 1 FROM videos v WHERE v.series_id = se.id AND v.missing = 0
            )
            ORDER BY sc.display_name, se.base_title
            """;
        var list = new List<OrphanEntry>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new OrphanEntry(r.GetInt64(0), r.GetString(1), r.GetString(2)));
        return list;
    }

    /// <summary>Sections (creators) that have zero playable (non-missing) videos.</summary>
    public IReadOnlyList<OrphanEntry> GetEmptyCreators()
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT sc.id, sc.display_name, sc.display_name
            FROM sections sc
            WHERE NOT EXISTS (
                SELECT 1 FROM videos v
                JOIN series se ON se.id = v.series_id
                WHERE se.section_id = sc.id AND v.missing = 0
            )
            ORDER BY sc.display_name
            """;
        var list = new List<OrphanEntry>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new OrphanEntry(r.GetInt64(0), r.GetString(1), r.GetString(2)));
        return list;
    }

    /// <summary>
    /// Removes a series and its video rows from the DB index ONLY.
    /// Never touches the filesystem — files reappear on the next scan.
    /// </summary>
    public void DeleteSeriesIndex(long seriesId)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        // CASCADE on videos.series_id deletes video rows automatically.
        cmd.CommandText = "DELETE FROM series WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", seriesId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Removes a section (creator) and all its series/video rows from the DB index ONLY.
    /// Never touches the filesystem — files reappear on the next scan.
    /// </summary>
    public void DeleteSectionIndex(long sectionId)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        // CASCADE on series.section_id → series rows gone → videos CASCADE from series.
        cmd.CommandText = "DELETE FROM sections WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", sectionId);
        cmd.ExecuteNonQuery();
    }
}
