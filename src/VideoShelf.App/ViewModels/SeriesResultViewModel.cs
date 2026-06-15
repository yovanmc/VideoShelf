using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoShelf.App.Services;
using VideoShelf.Core.Models;

namespace VideoShelf.App.ViewModels;

/// <summary>
/// Card VM for a series search result. Exposes title, episode count label, cover image
/// (loaded via IThumbnailService), and an OpenCommand that raises OpenRequested with the
/// section id — same pattern as CreatorCardViewModel.
/// </summary>
public sealed partial class SeriesResultViewModel : ObservableObject
{
    private readonly SeriesResult _result;
    private readonly IThumbnailService? _thumbnails;
    private readonly IImageLoader? _imageLoader;

    public SeriesResultViewModel(SeriesResult result, IThumbnailService? thumbnails = null,
        IImageLoader? imageLoader = null)
    {
        _result = result;
        _thumbnails = thumbnails;
        _imageLoader = imageLoader;
    }

    public long SeriesId => _result.SeriesId;
    public long SectionId => _result.SectionId;
    public string Title => _result.Title;

    public string EpisodeCountLabel
        => _result.EpisodeCount == 1 ? "1 episode" : $"{_result.EpisodeCount} episodes";

    public string? ThumbnailSeedPath => _result.ThumbnailSeedPath;

    [ObservableProperty]
    private ImageSource? _cover;

    /// <summary>
    /// Raised when the card is opened. The host wires this to OpenSectionAsync(SectionId)
    /// so it navigates to the creator page that contains this series.
    /// </summary>
    public event Action<long>? OpenRequested;

    [RelayCommand]
    private void Open() => OpenRequested?.Invoke(_result.SectionId);

    /// <summary>Loads the cover image from the thumbnail service (fail-safe, never throws).</summary>
    public async Task LoadImageAsync(CancellationToken ct)
    {
        if (_thumbnails is null || _imageLoader is null) return;
        if (string.IsNullOrWhiteSpace(ThumbnailSeedPath)) return;

        var path = await _thumbnails.GetThumbnailPathAsync(ThumbnailSeedPath!, ct);
        Cover = _imageLoader.Load(path, decodePixelWidth: 200);
    }
}
