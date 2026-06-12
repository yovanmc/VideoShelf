using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoShelf.Core.Discovery;

namespace VideoShelf.App.ViewModels.Discovery;

public sealed partial class RecencyCardViewModel(RecencyItem item) : ObservableObject
{
    public long VideoId => item.VideoId;
    public long SeriesId => item.SeriesId;
    public string SeriesTitle => item.SeriesTitle;
    public string EpisodeLabel => item.IsStandalone ? item.SeriesTitle : $"Episode {item.EpisodeNo}";
    public bool Watched => item.Watched;
    public string? ThumbnailSeedPath => item.ThumbnailSeedPath;

    [ObservableProperty] private string? thumbnailPath;

    public event EventHandler? PlayInvoked;
    [RelayCommand] private void Play() => PlayInvoked?.Invoke(this, EventArgs.Empty);
}
