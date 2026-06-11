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

    public event System.EventHandler? UnwatchedChanged;
    public event System.EventHandler<EpisodeView>? PlayRequested;

    partial void OnUnwatchedCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasUnwatched));
        UnwatchedChanged?.Invoke(this, System.EventArgs.Empty);
    }

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
        var rows = await Task.Run(() => library.GetEpisodes(summary.SeriesId), cancellationToken);
        Episodes.Clear();
        foreach (var row in rows)
        {
            var ep = new EpisodeViewModel(row, watch);
            ep.WatchedChanged += (_, _) => Refresh();
            ep.PlayRequested += (_, e) => PlayRequested?.Invoke(this, e);
            Episodes.Add(ep);
        }
    }

    public async Task LoadThumbnailAsync(CancellationToken cancellationToken)
    {
        if (summary.ThumbnailSeedPath is null)
            return;
        ThumbnailPath = await thumbnails.GetThumbnailPathAsync(summary.ThumbnailSeedPath, cancellationToken);
    }
}
