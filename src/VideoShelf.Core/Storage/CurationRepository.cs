using System;
using System.Collections.Generic;
using VideoShelf.Core.Discovery;

namespace VideoShelf.Core.Storage;

/// <summary>Reads and writes per-video favorites and star ratings.</summary>
public sealed class CurationRepository(VideoShelfDb db)
{
    public bool IsFavorite(long videoId)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT is_favorite FROM videos WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", videoId);
        var result = cmd.ExecuteScalar();
        return result is long l && l != 0;
    }

    public void SetFavorite(long videoId, bool value)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE videos SET is_favorite = $v WHERE id = $id";
        cmd.Parameters.AddWithValue("$v", value ? 1 : 0);
        cmd.Parameters.AddWithValue("$id", videoId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Returns the rating for a video (0..5).</summary>
    public int GetRating(long videoId)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT rating FROM videos WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", videoId);
        var result = cmd.ExecuteScalar();
        return result is long l ? (int)l : 0;
    }

    /// <summary>Sets the rating for a video (clamped to 0..5).</summary>
    public void SetRating(long videoId, int rating)
    {
        var clamped = Math.Max(0, Math.Min(5, rating));
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE videos SET rating = $r WHERE id = $id";
        cmd.Parameters.AddWithValue("$r", clamped);
        cmd.Parameters.AddWithValue("$id", videoId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Returns favorite videos ordered by most-recently-resumed/added, newest first.
    /// Uses the same recency projection as <see cref="DiscoveryRepository"/>.
    /// </summary>
    public IReadOnlyList<RecencyItem> GetFavorites(int limit)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT v.id, v.series_id, s.section_id, s.base_title, s.is_standalone,
                   v.episode_no, v.watched, v.thumbnail_path
            FROM videos v
            JOIN series s ON s.id = v.series_id
            WHERE v.is_favorite = 1 AND v.missing = 0
            ORDER BY COALESCE(v.resume_updated_at, v.added_at) DESC, v.id DESC
            LIMIT $lim
            """;
        cmd.Parameters.AddWithValue("$lim", limit);
        var list = new List<RecencyItem>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new RecencyItem(
                VideoId: r.GetInt64(0),
                SeriesId: r.GetInt64(1),
                SectionId: r.GetInt64(2),
                SeriesTitle: r.GetString(3),
                IsStandalone: r.GetInt64(4) != 0,
                EpisodeNo: r.GetInt32(5),
                Watched: r.GetInt64(6) != 0,
                ThumbnailSeedPath: r.IsDBNull(7) ? null : r.GetString(7)));
        }
        return list;
    }
}
