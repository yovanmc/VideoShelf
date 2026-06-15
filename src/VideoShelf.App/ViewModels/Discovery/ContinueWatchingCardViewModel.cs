using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoShelf.App.Services;
using VideoShelf.Core.Discovery;

namespace VideoShelf.App.ViewModels.Discovery;

public sealed partial class ContinueWatchingCardViewModel : ObservableObject
{
    private readonly ContinueWatchingItem _item;
    private readonly IThumbnailService? _thumbnails;
    private readonly IImageLoader? _imageLoader;

    public ContinueWatchingCardViewModel(ContinueWatchingItem item, IThumbnailService? thumbnails = null,
        IImageLoader? imageLoader = null)
    {
        _item = item;
        _thumbnails = thumbnails;
        _imageLoader = imageLoader;
    }

    public long VideoId => _item.VideoId;
    public long SeriesId => _item.SeriesId;
    public string SeriesTitle => _item.SeriesTitle;
    public string EpisodeLabel => _item.IsStandalone ? _item.SeriesTitle : $"Episode {_item.EpisodeNo}";
    public string? ThumbnailSeedPath => _item.ThumbnailSeedPath;
    public double ProgressFraction =>
        _item.Duration is > 0 ? Math.Clamp(_item.ResumePosition / _item.Duration.Value, 0, 1) : 0;

    /// <summary>True when there is non-zero progress and the video is not yet fully watched.
    /// Drives the progress-% text overlay on the card (non-color state cue).</summary>
    public bool HasProgress => ProgressFraction > 0 && !IsWatched;

    /// <summary>True when the item has been watched (resume position equals or exceeds duration).
    /// Used to show the watched-checkmark badge on the card.</summary>
    public bool IsWatched => _item.Duration is > 0 && _item.ResumePosition >= _item.Duration.Value;

    [ObservableProperty] private string? thumbnailPath;

    /// <summary>Frozen ImageSource for the card cover; loaded asynchronously via
    /// <see cref="LoadImageAsync"/>. Null until loaded (shows placeholder).</summary>
    [ObservableProperty] private ImageSource? _cover;

    public event EventHandler? PlayInvoked;
    [RelayCommand] private void Play() => PlayInvoked?.Invoke(this, EventArgs.Empty);

    /// <summary>Loads the cover image from the thumbnail service (fail-safe, never throws).</summary>
    public async Task LoadImageAsync(CancellationToken ct)
    {
        if (_thumbnails is null || _imageLoader is null) return;
        if (string.IsNullOrWhiteSpace(ThumbnailSeedPath)) return;

        var path = await _thumbnails.GetThumbnailPathAsync(ThumbnailSeedPath!, ct);
        ThumbnailPath = path;
        Cover = _imageLoader.Load(path, decodePixelWidth: 200);
    }
}
