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

    /// <summary>True when there is non-zero progress and the video is not yet fully watched.
    /// Drives the progress-% text overlay on the card (non-color state cue).</summary>
    public bool HasProgress => ProgressFraction > 0 && !IsWatched;

    /// <summary>True when the item has been watched (resume position equals or exceeds duration).
    /// Used to show the watched-checkmark badge on the card.</summary>
    public bool IsWatched => item.Duration is > 0 && item.ResumePosition >= item.Duration.Value;

    [ObservableProperty] private string? thumbnailPath;

    public event EventHandler? PlayInvoked;
    [RelayCommand] private void Play() => PlayInvoked?.Invoke(this, EventArgs.Empty);
}
