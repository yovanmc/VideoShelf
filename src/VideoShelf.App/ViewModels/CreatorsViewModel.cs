using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoShelf.App.Services;
using VideoShelf.Core.Models;
using VideoShelf.Core.Search;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.ViewModels;

public partial class CreatorsViewModel : ObservableObject, IBulkSelectionSource
{
    private readonly LibraryRepository _library;
    private readonly CreatorArtRepository _art;
    private readonly IThumbnailService _thumbnails;
    private readonly SettingsRepository? _settings;
    private readonly IImageLoader? _imageLoader;

    // ── ICollectionView for live filtering ───────────────────────────────────
    // Null in test environments (no WPF Dispatcher); non-null in the real app.
    // The ListBox in MainWindow.xaml binds ItemsSource to Creators directly —
    // WPF automatically uses the default view, so the filter applies at runtime
    // without any extra binding change.
    private readonly ICollectionView? _creatorsView;

    public CreatorsViewModel(LibraryRepository library, CreatorArtRepository art, IThumbnailService thumbnails,
        SettingsRepository? settings = null, IImageLoader? imageLoader = null)
    {
        _library = library;
        _art = art;
        _thumbnails = thumbnails;
        _settings = settings;
        _imageLoader = imageLoader;
        Selection.SelectedItems.CollectionChanged += (_, _) => SelectionChanged?.Invoke(this, EventArgs.Empty);

        // Only attach the ICollectionView when a WPF Dispatcher is available.
        // In unit tests there is no Application/Dispatcher, and the ObservableCollection
        // is mutated from background threads in LoadAsync, which would crash the view.
        if (System.Windows.Application.Current is not null)
        {
            _creatorsView = CollectionViewSource.GetDefaultView(Creators);
            _creatorsView.Filter = FilterPredicate;
        }

        // Load persisted density + view mode if settings are available.
        if (_settings is not null)
        {
            _density   = _settings.GetBrowseDensity();
            _viewMode  = _settings.GetBrowseViewMode();
        }
    }

    // ── Filter bar (F1) ──────────────────────────────────────────────────────

    [ObservableProperty]
    private string _filterText = "";

    [ObservableProperty]
    private bool _isFilterVisible;

    [RelayCommand]
    private void ToggleFilter()
    {
        IsFilterVisible = !IsFilterVisible;
        if (!IsFilterVisible)
            FilterText = "";
    }

    [RelayCommand]
    private void ClearFilter() => FilterText = "";

    partial void OnFilterTextChanged(string value) => _creatorsView?.Refresh();

    /// <summary>
    /// Pure filter predicate — unit-testable directly.
    /// Matches the creator name case-insensitively against FilterText.
    /// Creator tags are not on the card VM (no tag fetch per keystroke);
    /// this is name-only as documented.
    /// </summary>
    public static bool CreatorMatchesPredicate(CreatorCardViewModel card, string filterText)
    {
        if (string.IsNullOrWhiteSpace(filterText)) return true;
        return card.Name.Contains(filterText, StringComparison.OrdinalIgnoreCase);
    }

    private bool FilterPredicate(object item)
        => item is CreatorCardViewModel card && CreatorMatchesPredicate(card, FilterText);

    // ── Density (F2) ─────────────────────────────────────────────────────────

    [ObservableProperty]
    private BrowseDensity _density = BrowseDensity.Normal;

    partial void OnDensityChanged(BrowseDensity value)
        => _settings?.SetBrowseDensity(value);

    // ── View mode (F2) ───────────────────────────────────────────────────────

    [ObservableProperty]
    private BrowseViewMode _viewMode = BrowseViewMode.Grid;

    partial void OnViewModeChanged(BrowseViewMode value)
        => _settings?.SetBrowseViewMode(value);

    // ── Density commands (for ToggleButton click bindings in XAML) ───────────

    [RelayCommand]
    private void SetDensityCompact()  => Density = BrowseDensity.Compact;

    [RelayCommand]
    private void SetDensityNormal()   => Density = BrowseDensity.Normal;

    [RelayCommand]
    private void SetDensitySpacious() => Density = BrowseDensity.Spacious;

    // ── View-mode commands ────────────────────────────────────────────────────

    [RelayCommand]
    private void SetViewModeGrid() => ViewMode = BrowseViewMode.Grid;

    [RelayCommand]
    private void SetViewModeList() => ViewMode = BrowseViewMode.List;

    /// <summary>
    /// B3 — Expands the currently selected creators into their constituent (non-missing) video ids.
    /// Uses <see cref="LibraryRepository.GetEpisodesForSection"/> so no new query method is needed.
    /// Call this each time the selection changes to feed <see cref="BulkActionBarViewModel.SetVideoIds"/>.
    /// </summary>
    public IReadOnlyList<long> GetSelectedVideoIds()
    {
        var ids = new List<long>();
        foreach (var card in Selection.SelectedItems)
        {
            var episodes = _library.GetEpisodesForSection(card.SectionId);
            ids.AddRange(episodes.Select(e => e.VideoId));
        }
        return ids;
    }

    /// <summary>True while LoadAsync is in progress; used to show the skeleton overlay.</summary>
    [ObservableProperty]
    private bool _isLoading;

    public ObservableCollection<CreatorCardViewModel> Creators { get; } = new();

    /// <summary>Per-page selection state (enter/exit mode, selected set, commands).</summary>
    public SelectionViewModel<CreatorCardViewModel> Selection { get; } = new();

    // ── IBulkSelectionSource ─────────────────────────────────────────────────
    bool IBulkSelectionSource.HasSelection => Selection.HasSelection;
    IReadOnlyList<long> IBulkSelectionSource.GetSelectedVideoIds() => GetSelectedVideoIds();
    public event EventHandler? SelectionChanged;
    void IBulkSelectionSource.ClearSelection() => Selection.ClearSelectionCommand.Execute(null);
    void IBulkSelectionSource.ExitSelectionMode() => Selection.ExitSelectionModeCommand.Execute(null);

    /// <summary>Raised when a creator card is activated (forwarded to the host nav).</summary>
    public event Action<long>? OpenCreatorRequested;

    // ── A–Z jump-list (G1) ───────────────────────────────────────────────────

    /// <summary>
    /// All 26 letters A–Z, each tagged with whether at least one creator name
    /// starts with that letter.  Updated whenever the Creators collection is rebuilt
    /// in LoadAsync.  The jump strip ItemsControl binds to this list so disabled
    /// letters are visually greyed out.
    /// </summary>
    [ObservableProperty]
    private IReadOnlyList<JumpLetterItem> _jumpLetters = BuildAllLetters(new HashSet<char>());

    /// <summary>
    /// Raised by the VM when the View should scroll the creator ListBox to the first
    /// item whose name starts with <see cref="JumpLetterArgs.Letter"/>.
    /// The View resolves the target item via <see cref="JumpListIndex.FirstIndexForLetter"/>
    /// and calls <c>ListBox.ScrollIntoView(item)</c>.
    /// </summary>
    public event EventHandler<JumpLetterArgs>? JumpToLetterRequested;

    /// <summary>Scrolls the creator grid to the first creator starting with <paramref name="letter"/>.</summary>
    [RelayCommand]
    private void JumpToLetter(char letter)
    {
        // Only raise the event; the View owns the ListBox ref (no WPF dep in VM).
        JumpToLetterRequested?.Invoke(this, new JumpLetterArgs(letter));
    }

    private void RefreshAvailableLetters()
    {
        var names = Creators.Select(c => c.Name).ToList();
        var available = JumpListIndex.AvailableLetters(names);
        var availSet = new HashSet<char>(available);
        JumpLetters = BuildAllLetters(availSet);
    }

    private static IReadOnlyList<JumpLetterItem> BuildAllLetters(ISet<char> available)
    {
        var items = new JumpLetterItem[26];
        for (var i = 0; i < 26; i++)
        {
            var letter = (char)('A' + i);
            items[i] = new JumpLetterItem(letter, available.Contains(letter));
        }
        return items;
    }

    public async Task LoadAsync(CancellationToken ct)
    {
        IsLoading = true;
        try
        {
            // Heavy work off the UI thread; resume on the captured context to mutate the UI-bound collection.
            // NOTE: do NOT use ConfigureAwait(false) on this chain (the Cross-thread ObservableCollection gotcha).
            // GetArtPath is also resolved in the same background Task.Run to avoid per-card SQLite round-trips
            // on the UI thread.
            var cards = await Task.Run(() =>
            {
                var summaries = _library.GetSectionSummaries();
                var result = new System.Collections.Generic.List<(VideoShelf.Core.Models.SectionSummary Summary, string? OverridePath)>(summaries.Count);
                foreach (var s in summaries)
                    result.Add((s, _art.GetArtPath(s.SectionId)));
                return result;
            }, ct);

            // Unsubscribe from all existing cards before clearing.
            foreach (var existing in Creators)
                existing.PropertyChanged -= OnCardPropertyChanged;

            Creators.Clear();
            foreach (var (summary, overridePath) in cards)
            {
                var card = new CreatorCardViewModel(summary, overridePath, _thumbnails, _imageLoader);
                card.OpenRequested += id => OpenCreatorRequested?.Invoke(id);
                // Subscribe to route IsSelected changes into the Selection VM (no back-ref in card).
                card.PropertyChanged += OnCardPropertyChanged;
                Creators.Add(card);
                await card.LoadImageAsync(ct);
            }

            RefreshAvailableLetters();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void OnCardPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CreatorCardViewModel.IsSelected) &&
            sender is CreatorCardViewModel card)
        {
            Selection.OnItemSelectionChanged(card);
        }
    }
}
