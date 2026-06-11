using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using VideoShelf.App.Services;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.ViewModels;

public sealed partial class SeriesViewModel(
    SeriesSummary summary,
    LibraryRepository library,
    WatchRepository watch,
    IThumbnailService thumbnails) : ObservableObject
{
    public long SeriesId => summary.SeriesId;
    public string BaseTitle => summary.BaseTitle;
    public bool IsStandalone => summary.IsStandalone;
    public int EpisodeCount => summary.EpisodeCount;

    public ObservableCollection<EpisodeViewModel> Episodes { get; } = [];

    [ObservableProperty]
    private int _unwatchedCount = summary.UnwatchedCount;

    [ObservableProperty]
    private string? _thumbnailPath;

    public bool HasUnwatched => UnwatchedCount > 0;

    partial void OnUnwatchedCountChanged(int value) => OnPropertyChanged(nameof(HasUnwatched));

    /// <summary>Recomputes the unwatched badge from the DB (after a watched toggle).</summary>
    public void Refresh()
    {
        var fresh = 0;
        foreach (var e in library.GetEpisodes(summary.SeriesId))
            if (!e.Watched) fresh++;
        UnwatchedCount = fresh;
    }

    public async Task LoadEpisodesAsync(CancellationToken cancellationToken)
    {
        var rows = await Task.Run(() => library.GetEpisodes(summary.SeriesId), cancellationToken)
            .ConfigureAwait(false);
        Episodes.Clear();
        foreach (var row in rows)
            Episodes.Add(new EpisodeViewModel(row, watch));
    }

    public async Task LoadThumbnailAsync(CancellationToken cancellationToken)
    {
        if (summary.ThumbnailSeedPath is null)
            return;
        ThumbnailPath = await thumbnails.GetThumbnailPathAsync(summary.ThumbnailSeedPath, cancellationToken)
            .ConfigureAwait(false);
    }
}
