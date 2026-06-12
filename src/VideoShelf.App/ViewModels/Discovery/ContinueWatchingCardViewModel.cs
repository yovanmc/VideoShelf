using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoShelf.Core.Discovery;

namespace VideoShelf.App.ViewModels.Discovery;

public sealed partial class ContinueWatchingCardViewModel(ContinueWatchingItem item) : ObservableObject
{
    public long VideoId => item.VideoId;
    public long SeriesId => item.SeriesId;
    public string SeriesTitle => item.SeriesTitle;
    public string EpisodeLabel => item.IsStandalone ? item.SeriesTitle : $"Episode {item.EpisodeNo}";
    public string? ThumbnailSeedPath => item.ThumbnailSeedPath;
    public double ProgressFraction =>
        item.Duration is > 0 ? Math.Clamp(item.ResumePosition / item.Duration.Value, 0, 1) : 0;

    public string? ChapterLabel { get; init; }
    public bool HasChapter => !string.IsNullOrEmpty(ChapterLabel);

    [ObservableProperty] private string? thumbnailPath;

    public event EventHandler? PlayInvoked;
    [RelayCommand] private void Play() => PlayInvoked?.Invoke(this, EventArgs.Empty);
}
