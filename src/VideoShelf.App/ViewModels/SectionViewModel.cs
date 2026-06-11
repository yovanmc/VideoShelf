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
        var unwatched = 0;
        foreach (var s in summaries)
        {
            SeriesList.Add(new SeriesViewModel(s, library, watch, thumbnails));
            unwatched += s.UnwatchedCount;
        }
        UnwatchedCount = unwatched;
    }
}
