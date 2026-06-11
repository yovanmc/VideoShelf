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
    }

    [ObservableProperty]
    private bool _autoAdvanceEpisodes;

    partial void OnAutoAdvanceEpisodesChanged(bool value)
        => _settings.SetAutoAdvanceEpisodes(value);
}
