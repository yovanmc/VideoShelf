namespace VideoShelf.Core.Storage;

/// <summary>
/// Per-video and per-series art overrides. DB-only: stores a path to a user-chosen
/// image; never copies into or writes to library folders.
/// </summary>
public sealed class ItemArtRepository(VideoShelfDb db)
{
    // ── video_art ──────────────────────────────────────────────────────────

    public string? GetVideoArt(long videoId)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT image_path FROM video_art WHERE video_id = $id";
        cmd.Parameters.AddWithValue("$id", videoId);
        var result = cmd.ExecuteScalar();
        return result is string s ? s : null;
    }

    public void SetVideoArt(long videoId, string imagePath)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO video_art (video_id, image_path) VALUES ($id, $path)
            ON CONFLICT(video_id) DO UPDATE SET image_path = excluded.image_path
            """;
        cmd.Parameters.AddWithValue("$id", videoId);
        cmd.Parameters.AddWithValue("$path", imagePath);
        cmd.ExecuteNonQuery();
    }

    public void ClearVideoArt(long videoId)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM video_art WHERE video_id = $id";
        cmd.Parameters.AddWithValue("$id", videoId);
        cmd.ExecuteNonQuery();
    }

    // ── series_art ─────────────────────────────────────────────────────────

    public string? GetSeriesArt(long seriesId)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT image_path FROM series_art WHERE series_id = $id";
        cmd.Parameters.AddWithValue("$id", seriesId);
        var result = cmd.ExecuteScalar();
        return result is string s ? s : null;
    }

    public void SetSeriesArt(long seriesId, string imagePath)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO series_art (series_id, image_path) VALUES ($id, $path)
            ON CONFLICT(series_id) DO UPDATE SET image_path = excluded.image_path
            """;
        cmd.Parameters.AddWithValue("$id", seriesId);
        cmd.Parameters.AddWithValue("$path", imagePath);
        cmd.ExecuteNonQuery();
    }

    public void ClearSeriesArt(long seriesId)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM series_art WHERE series_id = $id";
        cmd.Parameters.AddWithValue("$id", seriesId);
        cmd.ExecuteNonQuery();
    }
}
