using CommunityToolkit.Mvvm.ComponentModel;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsRepository _settings;

    public SettingsViewModel(SettingsRepository settings)
    {
        _settings = settings;
        _autoAdvanceEpisodes = settings.GetAutoAdvanceEpisodes();
        RefreshLastScan();
        // Restore persisted scan summary (survives restart).
        _lastScanSummaryText = settings.GetLastScanSummary() ?? string.Empty;
    }

    [ObservableProperty]
    private bool _autoAdvanceEpisodes;

    partial void OnAutoAdvanceEpisodesChanged(bool value)
        => _settings.SetAutoAdvanceEpisodes(value);

    [ObservableProperty]
    private string _lastScanText = "Never scanned";

    /// <summary>
    /// Formatted scan-diff string ("Added 12 · updated 3 · restored 1 · missing 1").
    /// Empty until the first scan; persisted across restarts via <c>last_scan_summary</c>.
    /// </summary>
    [ObservableProperty]
    private string _lastScanSummaryText = string.Empty;

    private void RefreshLastScan()
    {
        var t = _settings.GetLastScanUtc();
        LastScanText = t is null ? "Never scanned" : $"Last scanned {t.Value.ToLocalTime():g}";
    }

    /// <summary>
    /// Records a completed scan, persists the diff summary, and refreshes displayed text.
    /// </summary>
    public void MarkScanned(string scanSummary)
    {
        _settings.SetLastScanUtc(DateTime.UtcNow);
        _settings.SetLastScanSummary(scanSummary);
        RefreshLastScan();
        LastScanSummaryText = scanSummary;
    }
}
