namespace VideoShelf.Core.Storage;

/// <summary>
/// Per-creator (section) art override. DB-only: stores a path to a user-chosen
/// image; never copies into or writes to library folders.
/// </summary>
public sealed class CreatorArtRepository(VideoShelfDb db)
{
    public string? GetArtPath(long sectionId)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT image_path FROM creator_art WHERE section_id = $id";
        cmd.Parameters.AddWithValue("$id", sectionId);
        var result = cmd.ExecuteScalar();
        return result is string s ? s : null;
    }

    public void SetArtPath(long sectionId, string imagePath)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO creator_art (section_id, image_path) VALUES ($id, $path)
            ON CONFLICT(section_id) DO UPDATE SET image_path = excluded.image_path
            """;
        cmd.Parameters.AddWithValue("$id", sectionId);
        cmd.Parameters.AddWithValue("$path", imagePath);
        cmd.ExecuteNonQuery();
    }

    public void ClearArtPath(long sectionId)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM creator_art WHERE section_id = $id";
        cmd.Parameters.AddWithValue("$id", sectionId);
        cmd.ExecuteNonQuery();
    }
}
