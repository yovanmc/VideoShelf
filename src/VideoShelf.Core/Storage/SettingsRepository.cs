namespace VideoShelf.Core.Storage;

/// <summary>Density level for the Browse creator grid.</summary>
public enum BrowseDensity { Compact, Normal, Spacious }

/// <summary>View mode for the Browse creator grid.</summary>
public enum BrowseViewMode { Grid, List }

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

    public const string LastScanUtcKey = "last_scan_utc";

    /// <summary>Returns the last successful library-scan time (UTC), or null if never scanned.</summary>
    public DateTime? GetLastScanUtc()
    {
        var raw = GetString(LastScanUtcKey, "");
        return DateTime.TryParse(raw, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt) ? dt : null;
    }

    public void SetLastScanUtc(DateTime utc)
        => SetString(LastScanUtcKey, utc.ToString("o"));

    // ── Last scan summary ─────────────────────────────────────────────────────
    public const string LastScanSummaryKey = "last_scan_summary";

    /// <summary>Returns the last scan-diff summary string, or null if never scanned.</summary>
    public string? GetLastScanSummary()
    {
        var raw = GetString(LastScanSummaryKey, "");
        return string.IsNullOrEmpty(raw) ? null : raw;
    }

    public void SetLastScanSummary(string summary)
        => SetString(LastScanSummaryKey, summary);

    // ── Browse density ────────────────────────────────────────────────────────
    public const string BrowseDensityKey = "browse_density";

    /// <summary>The density level for the Browse creator grid. Defaults to <see cref="BrowseDensity.Normal"/>.</summary>
    public BrowseDensity GetBrowseDensity()
    {
        var raw = GetString(BrowseDensityKey, "");
        return Enum.TryParse<BrowseDensity>(raw, out var v) ? v : BrowseDensity.Normal;
    }

    public void SetBrowseDensity(BrowseDensity value)
        => SetString(BrowseDensityKey, value.ToString());

    // ── Browse view mode ──────────────────────────────────────────────────────
    public const string BrowseViewModeKey = "browse_view_mode";

    /// <summary>The view mode for the Browse creator grid. Defaults to <see cref="BrowseViewMode.Grid"/>.</summary>
    public BrowseViewMode GetBrowseViewMode()
    {
        var raw = GetString(BrowseViewModeKey, "");
        return Enum.TryParse<BrowseViewMode>(raw, out var v) ? v : BrowseViewMode.Grid;
    }

    public void SetBrowseViewMode(BrowseViewMode value)
        => SetString(BrowseViewModeKey, value.ToString());
}
