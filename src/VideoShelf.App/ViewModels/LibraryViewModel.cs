using System.Collections.ObjectModel;
using System.Linq;
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

    public event System.EventHandler<VideoShelf.Core.Models.EpisodeView>? PlayRequested;

    [ObservableProperty]
    private SectionViewModel? _selectedSection;

    [ObservableProperty]
    private BrowseSort _sortMode = BrowseSort.Name;

    [ObservableProperty]
    private string _searchText = "";

    public async Task LoadSectionsAsync()
    {
        var summaries = await Task.Run(library.GetSectionSummaries);
        Sections.Clear();
        foreach (var s in summaries)
        {
            var sectionVm = new SectionViewModel(s, library, watch, thumbnails);
            sectionVm.PlayRequested += (_, e) => PlayRequested?.Invoke(this, e);
            Sections.Add(sectionVm);
        }
    }

    public async Task SelectSectionAsync(SectionViewModel? section)
    {
        SelectedSection = section;
        if (section is not null)
            await section.LoadSeriesAsync(SortMode, CancellationToken.None);
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
        var hits = await Task.Run(() => library.Search(query));
        SearchResults.Clear();
        foreach (var h in hits)
            SearchResults.Add(h);
    }

    /// <summary>
    /// Selects the section that owns <paramref name="hit"/> and clears the search box,
    /// implementing spec §6 "selecting a search result jumps to that item".
    /// </summary>
    [RelayCommand]
    public async Task NavigateToHit(SearchHit hit)
    {
        var target = Sections.FirstOrDefault(s => s.SectionId == hit.SectionId);
        if (target is not null)
            await SelectSectionAsync(target);
        SearchText = "";
    }

    /// <summary>Test/affordance hook: awaits the most recently started async reload/search.</summary>
    public Task WaitForIdleAsync() => _pending;

    [RelayCommand]
    private async Task Refresh() => await LoadSectionsAsync();
}
