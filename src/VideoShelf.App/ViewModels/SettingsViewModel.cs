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
    }

    [ObservableProperty]
    private bool _autoAdvanceEpisodes;

    partial void OnAutoAdvanceEpisodesChanged(bool value)
        => _settings.SetAutoAdvanceEpisodes(value);

    [ObservableProperty]
    private string _lastScanText = "Never scanned";

    private void RefreshLastScan()
    {
        var t = _settings.GetLastScanUtc();
        LastScanText = t is null ? "Never scanned" : $"Last scanned {t.Value.ToLocalTime():g}";
    }

    /// <summary>Records a completed scan and refreshes the displayed time.</summary>
    public void MarkScanned()
    {
        _settings.SetLastScanUtc(DateTime.UtcNow);
        RefreshLastScan();
    }
}
