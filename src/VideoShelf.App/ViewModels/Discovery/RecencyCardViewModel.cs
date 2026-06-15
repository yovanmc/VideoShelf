using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoShelf.App.Services;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Discovery;

namespace VideoShelf.App.ViewModels.Discovery;

public sealed partial class RecencyCardViewModel : ObservableObject, ISelectableCard
{
    private readonly RecencyItem _item;
    private readonly IThumbnailService? _thumbnails;
    private readonly IImageLoader? _imageLoader;

    public RecencyCardViewModel(RecencyItem item, IThumbnailService? thumbnails = null,
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
    public bool Watched => _item.Watched;

    /// <summary>Alias for <see cref="Watched"/>: used by VideoCard's watched-checkmark badge
    /// so the card template can bind a uniform <c>IsWatched</c> property across all card VMs.</summary>
    public bool IsWatched => _item.Watched;

    /// <summary>False for recency cards (no resume position exposed): the progress bar
    /// and %-text overlay are not applicable here.</summary>
    public bool HasProgress => false;

    public string? ThumbnailSeedPath => _item.ThumbnailSeedPath;
    [ObservableProperty] private string? thumbnailPath;

    /// <summary>Frozen ImageSource for the card cover; loaded asynchronously via
    /// <see cref="LoadImageAsync"/>. Null until loaded (shows placeholder).</summary>
    [ObservableProperty] private ImageSource? _cover;

    /// <summary>True when this card is selected in the multi-select grid.
    /// The hosting VM subscribes to PropertyChanged and routes changes to
    /// <see cref="SelectionViewModel{T}.OnItemSelectionChanged"/> — no back-ref is stored here.</summary>
    [ObservableProperty] private bool isSelected;

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
