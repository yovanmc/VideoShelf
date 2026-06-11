using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoShelf.App.Services;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.ViewModels;

public sealed partial class LibraryViewModel(
    LibraryRepository library,
    WatchRepository watch,
    IThumbnailService thumbnails) : ObservableObject
{
    private Task _pending = Task.CompletedTask;

    public ObservableCollection<SectionViewModel> Sections { get; } = [];
    public ObservableCollection<SearchHit> SearchResults { get; } = [];

    [ObservableProperty]
    private SectionViewModel? _selectedSection;

    [ObservableProperty]
    private BrowseSort _sortMode = BrowseSort.Name;

    [ObservableProperty]
    private string _searchText = "";

    public async Task LoadSectionsAsync()
    {
        var summaries = await Task.Run(library.GetSectionSummaries).ConfigureAwait(false);
        Sections.Clear();
        foreach (var s in summaries)
            Sections.Add(new SectionViewModel(s, library, watch, thumbnails));
    }

    public async Task SelectSectionAsync(SectionViewModel? section)
    {
        SelectedSection = section;
        if (section is not null)
            await section.LoadSeriesAsync(SortMode, CancellationToken.None).ConfigureAwait(false);
    }

    partial void OnSortModeChanged(BrowseSort value)
    {
        if (SelectedSection is { } section)
            _pending = section.LoadSeriesAsync(value, CancellationToken.None);
    }

    partial void OnSearchTextChanged(string value)
    {
        _pending = RunSearchAsync(value);
    }

    private async Task RunSearchAsync(string query)
    {
        var hits = await Task.Run(() => library.Search(query)).ConfigureAwait(false);
        SearchResults.Clear();
        foreach (var h in hits)
            SearchResults.Add(h);
    }

    /// <summary>Test/affordance hook: awaits the most recently started async reload/search.</summary>
    public Task WaitForIdleAsync() => _pending;

    [RelayCommand]
    private async Task Refresh() => await LoadSectionsAsync();
}
