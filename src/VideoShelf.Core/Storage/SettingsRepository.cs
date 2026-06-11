namespace VideoShelf.Core.Storage;

/// <summary>Typed access to the app's key/value <c>settings</c> table.</summary>
public sealed class SettingsRepository(VideoShelfDb db)
{
    public const string AutoAdvanceKey = "auto_advance_episodes";

    public string GetString(string key, string fallback)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM settings WHERE key = $k";
        cmd.Parameters.AddWithValue("$k", key);
        var result = cmd.ExecuteScalar();
        return result is string s ? s : fallback;
    }

    public void SetString(string key, string value)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO settings(key, value) VALUES($k, $v)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value
            """;
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Whether reaching the end of an episode auto-advances to the next in its series. Default true.</summary>
    public bool GetAutoAdvanceEpisodes()
        => GetString(AutoAdvanceKey, "true") != "false";

    public void SetAutoAdvanceEpisodes(bool value)
        => SetString(AutoAdvanceKey, value ? "true" : "false");
}
