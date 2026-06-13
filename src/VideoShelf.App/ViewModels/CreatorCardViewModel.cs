using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoShelf.App.Services;
using VideoShelf.Core.Models;

namespace VideoShelf.App.ViewModels;

public partial class CreatorCardViewModel : ObservableObject, ISelectableCard
{
    private readonly SectionSummary _summary;
    private readonly string? _overrideArtPath;
    private readonly IThumbnailService _thumbnails;

    public CreatorCardViewModel(SectionSummary summary, string? overrideArtPath, IThumbnailService thumbnails)
    {
        _summary = summary;
        _overrideArtPath = overrideArtPath;
        _thumbnails = thumbnails;
    }

    public long SectionId => _summary.SectionId;
    public string Name => _summary.DisplayName;
    public int VideoCount => _summary.VideoCount;
    public string VideoCountLabel => $"{VideoCount} {(VideoCount == 1 ? "video" : "videos")}";

    [ObservableProperty]
    private string? _imagePath;

    /// <summary>True when this card is selected in the multi-select grid.
    /// The hosting VM subscribes to PropertyChanged and routes changes to
    /// <see cref="SelectionViewModel{T}.OnItemSelectionChanged"/> — no back-ref is stored here.</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Raised when the card is activated; the host opens the creator page.</summary>
    public event Action<long>? OpenRequested;

    [RelayCommand]
    private void Open() => OpenRequested?.Invoke(_summary.SectionId);

    /// <summary>Resolve the card image: user override wins, else representative frame.</summary>
    public async Task LoadImageAsync(CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(_overrideArtPath))
        {
            ImagePath = _overrideArtPath;
            return;
        }

        if (string.IsNullOrWhiteSpace(_summary.ThumbnailSeedPath))
        {
            ImagePath = null;
            return;
        }

        // Fail-safe: thumbnail service never throws, returns null on failure.
        ImagePath = await _thumbnails.GetThumbnailPathAsync(_summary.ThumbnailSeedPath!, ct);
    }
}
