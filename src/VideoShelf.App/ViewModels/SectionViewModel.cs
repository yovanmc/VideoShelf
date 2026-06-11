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
    IThumbnailService thumbnails) : ObservableObject
{
    public long SectionId => summary.SectionId;
    public string DisplayName => summary.DisplayName;

    public ObservableCollection<SeriesViewModel> SeriesList { get; } = [];

    [ObservableProperty]
    private int _unwatchedCount = summary.UnwatchedCount;

    public bool HasUnwatched => UnwatchedCount > 0;

    partial void OnUnwatchedCountChanged(int value) => OnPropertyChanged(nameof(HasUnwatched));

    public async Task LoadSeriesAsync(BrowseSort sort, CancellationToken cancellationToken)
    {
        var summaries = await Task.Run(
            () => library.GetSeriesSummaries(summary.SectionId, sort), cancellationToken)
            .ConfigureAwait(false);

        SeriesList.Clear();
        foreach (var s in summaries)
        {
            var seriesVm = new SeriesViewModel(s, library, watch, thumbnails);
            seriesVm.UnwatchedChanged += (_, _) => RecomputeUnwatched();
            SeriesList.Add(seriesVm);
            await seriesVm.LoadEpisodesAsync(cancellationToken).ConfigureAwait(false);
            await seriesVm.LoadThumbnailAsync(cancellationToken).ConfigureAwait(false);
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
