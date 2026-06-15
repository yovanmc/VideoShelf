using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using VideoShelf.App.Services;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.ViewModels;

public sealed partial class SectionViewModel(
    SectionSummary summary,
    LibraryRepository library,
    WatchRepository watch,
    IThumbnailService thumbnails,
    IImageLoader? imageLoader = null) : ObservableObject
{
    public long SectionId => summary.SectionId;
    public string DisplayName => summary.DisplayName;

    public ObservableCollection<SeriesViewModel> SeriesList { get; } = [];

    [ObservableProperty]
    private int _unwatchedCount = summary.UnwatchedCount;

    public bool HasUnwatched => UnwatchedCount > 0;

    partial void OnUnwatchedCountChanged(int value) => OnPropertyChanged(nameof(HasUnwatched));

    public event System.EventHandler<EpisodeView>? PlayRequested;

    public async Task LoadSeriesAsync(BrowseSort sort, CancellationToken cancellationToken)
    {
        var summaries = await Task.Run(
            () => library.GetSeriesSummaries(summary.SectionId, sort), cancellationToken);

        SeriesList.Clear();
        foreach (var s in summaries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var seriesVm = new SeriesViewModel(s, library, watch, thumbnails, imageLoader: imageLoader);
            seriesVm.UnwatchedChanged += (_, _) => RecomputeUnwatched();
            seriesVm.PlayRequested += (_, e) => PlayRequested?.Invoke(this, e);
            SeriesList.Add(seriesVm);
            await seriesVm.LoadEpisodesAsync(cancellationToken);
            await seriesVm.LoadThumbnailAsync(cancellationToken);
        }
        RecomputeUnwatched();
    }

    public void RecomputeUnwatched()
    {
        var total = 0;
        foreach (var s in SeriesList)
            total += s.UnwatchedCount;
        UnwatchedCount = total;
    }
}
