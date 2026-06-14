using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoShelf.App.Services;
using VideoShelf.App.ViewModels.Discovery;
using VideoShelf.Core.Models;

namespace VideoShelf.App.ViewModels;

public enum AppView { Home, Browse, SectionDetail, RenameTool, MultiRename, Search, Settings, Queue, SmartViews, Favorites, Watchlist, Playlists, History, Maintenance, DuplicateResolve }

public sealed partial class MainViewModel : ObservableObject
{
    private readonly SourcesViewModel _sources;
    private readonly LibraryViewModel _library;
    private readonly IScanCoordinator _scanCoordinator;
    private readonly PlayerViewModel _player;
    private readonly SettingsViewModel _settings;
    private readonly MediaBackfillService _backfill;
    private readonly ResolutionBackfillService? _resolutionBackfill;
    private readonly PlayQueueViewModel _playQueue;
    private readonly VideoShelf.Core.Storage.LibraryRepository _libraryRepo;

    public MainViewModel(
        SourcesViewModel sources,
        LibraryViewModel library,
        IScanCoordinator scanCoordinator,
        PlayerViewModel player,
        SettingsViewModel settings,
        DiscoveryViewModel discovery,
        SectionDetailViewModel sectionDetail,
        RenameToolViewModel renameTool,
        CreatorsViewModel creators,
        SearchViewModel search,
        MediaBackfillService backfill,
        PlayQueueViewModel playQueue,
        SmartViewsViewModel smartViews,
        FavoritesViewModel favorites,
        WatchlistViewModel watchlist,
        PlaylistsViewModel playlists,
        HistoryViewModel history,
        VideoShelf.Core.Storage.LibraryRepository libraryRepo,
        BulkActionBarViewModel? bulkBar = null,
        CommandPaletteViewModel? commandPalette = null,
        MultiRenameViewModel? multiRename = null,
        ResolutionBackfillService? resolutionBackfill = null,
        MaintenanceViewModel? maintenance = null)
    {
        _sources = sources;
        _library = library;
        _scanCoordinator = scanCoordinator;
        _player = player;
        _settings = settings;
        _backfill = backfill;
        _resolutionBackfill = resolutionBackfill;
        _playQueue = playQueue;
        _libraryRepo = libraryRepo;
        Maintenance = maintenance;
        SmartViews = smartViews;
        Favorites = favorites;
        Watchlist = watchlist;
        Playlists = playlists;
        History = history;
        BulkBar = bulkBar;
        CommandPalette = commandPalette;
        MultiRename = multiRename;
        if (CommandPalette is not null)
            CommandPalette.CloseRequested += (_, _) => IsCommandPaletteOpen = false;
        if (MultiRename is not null)
            MultiRename.CloseRequested += (_, _) => GoBack();

        _library.PlayRequested += (_, ep) => PlayEpisode(ep);
        _playQueue.PlayRequested += (_, ep) => OpenPlayer(ep);
        _player.PlaybackEnded += (_, finished) =>
        {
            var next = _playQueue.GetNextAfterEnd(finished);
            if (next is not null) OpenPlayer(next);
        };

        Discovery = discovery;
        SectionDetail = sectionDetail;
        RenameTool = renameTool;
        Creators = creators;
        Search = search;
        Discovery.PlayRequested += (_, e) => PlayEpisode(e);
        Discovery.SectionOpenRequested += async (_, id) => await OpenSectionAsync(id);
        SectionDetail.PlayRequested += (_, e) => PlayEpisode(e);
        SectionDetail.RenameRequested += async (_, s) => await OpenRenameToolAsync(s);
        SectionDetail.ResolveRequested += (_, resolveVm) => OpenDuplicateResolve(resolveVm);
        RenameTool.CloseRequested += (_, _) => GoBack();
        Creators.OpenCreatorRequested += async id => await OpenSectionAsync(id);
        Search.PlayRequested += (_, e) => PlayEpisode(e);
        Search.OpenCreatorRequested += async id => await OpenSectionAsync(id);
        Search.PropertyChanged += (_, e) =>
        {
            // Typing in the persistent search box drives the user into the Search view.
            if (e.PropertyName == nameof(SearchViewModel.Query) && !string.IsNullOrEmpty(Search.Query))
            {
                if (CurrentView != AppView.Search) PushNav(CurrentView);
                CurrentView = AppView.Search;
            }
        };
        Favorites.PlayRequested += (_, e) => PlayEpisode(e);
        Watchlist.PlayRequested += (_, e) => PlayEpisode(e);
        History.PlayRequested += (_, e) => PlayEpisode(e);
        Sources.Sources.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsLibraryEmpty));
    }

    public string Title => "VideoShelf";

    /// <summary>Bulk-action bar; null in test contexts that don't supply it (nullable-trailing-param pattern).</summary>
    public BulkActionBarViewModel? BulkBar { get; }

    /// <summary>Command palette VM; null in test contexts that don't supply it (nullable-trailing-param pattern).</summary>
    public CommandPaletteViewModel? CommandPalette { get; }

    /// <summary>Multi-series template rename VM; null in test contexts that don't supply it (nullable-trailing-param pattern).</summary>
    public MultiRenameViewModel? MultiRename { get; }

    /// <summary>True when the Ctrl+K command palette overlay is visible.</summary>
    [ObservableProperty]
    private bool _isCommandPaletteOpen;

    public SmartViewsViewModel SmartViews { get; }
    public FavoritesViewModel Favorites { get; }
    public WatchlistViewModel Watchlist { get; }
    public PlaylistsViewModel Playlists { get; }
    public HistoryViewModel History { get; }
    public SourcesViewModel Sources => _sources;
    public LibraryViewModel Library => _library;
    public PlayerViewModel Player => _player;
    public SettingsViewModel Settings => _settings;
    public PlayQueueViewModel PlayQueue => _playQueue;
    public DiscoveryViewModel Discovery { get; }
    public SectionDetailViewModel SectionDetail { get; }
    public RenameToolViewModel RenameTool { get; }
    public CreatorsViewModel Creators { get; }
    public SearchViewModel Search { get; }

    /// <summary>Maintenance dashboard VM; null in test contexts that don't supply it (nullable-trailing-param pattern).</summary>
    public MaintenanceViewModel? Maintenance { get; }

    /// <summary>
    /// Current duplicate-resolve VM; set when navigating to the <see cref="AppView.DuplicateResolve"/> view.
    /// Null otherwise.
    /// </summary>
    [ObservableProperty]
    private DuplicateResolveViewModel? _duplicateResolve;

    [ObservableProperty]
    private AppView _currentView = AppView.Home;

    partial void OnCurrentViewChanged(AppView oldValue, AppView newValue)
    {
        // Detach from the old source (optionally exit its selection mode).
        if (_activeSelectionSource is not null)
        {
            _activeSelectionSource.SelectionChanged -= OnActiveSourceSelectionChanged;
            _activeSelectionSource.ExitSelectionMode();
        }

        _activeSelectionSource = newValue switch
        {
            AppView.Browse       => Creators,
            AppView.SectionDetail => SectionDetail,
            AppView.Favorites    => Favorites,
            AppView.Watchlist    => Watchlist,
            AppView.Search       => Search,
            _                    => null
        };

        if (_activeSelectionSource is not null)
            _activeSelectionSource.SelectionChanged += OnActiveSourceSelectionChanged;

        OnPropertyChanged(nameof(ActiveSelectionSource));
        OnPropertyChanged(nameof(BulkBarVisible));
    }

    private void OnActiveSourceSelectionChanged(object? sender, EventArgs e)
        => OnPropertyChanged(nameof(BulkBarVisible));

    private IBulkSelectionSource? _activeSelectionSource;

    /// <summary>The selectable page VM that is currently active, or null for non-selectable pages.</summary>
    public IBulkSelectionSource? ActiveSelectionSource => _activeSelectionSource;

    /// <summary>True when the active page has a non-empty selection (drives bulk-bar visibility).</summary>
    public bool BulkBarVisible => _activeSelectionSource?.HasSelection == true;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private bool _isPlayerVisible;

    [ObservableProperty]
    private bool _isPictureInPicture;

    /// <summary>The inline player pane is shown only while playing AND not detached into the mini-player.</summary>
    public bool IsInlinePlayerVisible => IsPlayerVisible && !IsPictureInPicture;

    partial void OnIsPlayerVisibleChanged(bool value) => OnPropertyChanged(nameof(IsInlinePlayerVisible));
    partial void OnIsPictureInPictureChanged(bool value) => OnPropertyChanged(nameof(IsInlinePlayerVisible));

    private readonly System.Collections.Generic.Stack<AppView> _backStack = new();
    public bool CanGoBack => _backStack.Count > 0;

    /// <summary>True at first run / when no source folders are configured (drives the empty-state CTA).</summary>
    public bool IsLibraryEmpty => Sources.Sources.Count == 0;

    private void PushNav(AppView from)
    {
        if (_backStack.Count == 0 || _backStack.Peek() != from)
            _backStack.Push(from);
        OnPropertyChanged(nameof(CanGoBack));
    }

    private void ClearBack()
    {
        _backStack.Clear();
        OnPropertyChanged(nameof(CanGoBack));
    }

    [RelayCommand]
    private void GoBack()
    {
        if (_backStack.Count == 0) return;
        CurrentView = _backStack.Pop();
        OnPropertyChanged(nameof(CanGoBack));
    }

    [RelayCommand]
    private void ShowSettings() { ClearBack(); CurrentView = AppView.Settings; }

    /// <summary>Direct single-episode play: registers it as a non-explicit queue entry then opens the player.</summary>
    public void PlayEpisode(EpisodeView episode)
    {
        _playQueue.StartSingle(episode);
        OpenPlayer(episode);
    }

    private void OpenPlayer(EpisodeView episode)
    {
        IsPlayerVisible = true;
        _player.Open(episode);
    }

    [RelayCommand]
    private void ShowHome()   { ClearBack(); CurrentView = AppView.Home; }

    [RelayCommand]
    private void ShowBrowse() { ClearBack(); CurrentView = AppView.Browse; }

    [RelayCommand]
    private void ShowQueue()
    {
        PushNav(CurrentView);
        CurrentView = AppView.Queue;
    }

    [RelayCommand]
    private void ShowSmartViews()
    {
        SmartViews.Load();
        PushNav(CurrentView);
        CurrentView = AppView.SmartViews;
    }

    [RelayCommand]
    private void ShowFavorites()
    {
        Favorites.Load();
        PushNav(CurrentView);
        CurrentView = AppView.Favorites;
    }

    [RelayCommand]
    private void ShowWatchlist()
    {
        Watchlist.Load();
        PushNav(CurrentView);
        CurrentView = AppView.Watchlist;
    }

    [RelayCommand]
    private void ShowPlaylists()
    {
        Playlists.Load();
        PushNav(CurrentView);
        CurrentView = AppView.Playlists;
    }

    [RelayCommand]
    private void ShowHistory()
    {
        History.Load();
        PushNav(CurrentView);
        CurrentView = AppView.History;
    }

    [RelayCommand]
    private void ShowMaintenance()
    {
        Maintenance?.Load();
        PushNav(CurrentView);
        CurrentView = AppView.Maintenance;
    }

    /// <summary>
    /// Opens the duplicate compare/resolve screen for the given group.
    /// Wires the PlayRequested event so the owner can eyeball each clip.
    /// Returns to the creator page (SectionDetail) on Resolved.
    /// </summary>
    private void OpenDuplicateResolve(DuplicateResolveViewModel vm)
    {
        vm.PlayRequested += (_, path) =>
        {
            // Route through the existing player: look up the episode by path.
            var ep = _libraryRepo.GetEpisodeByPath(path);
            if (ep is not null) PlayEpisode(ep);
        };
        vm.Resolved += (_, _) =>
        {
            DuplicateResolve = null;
            GoBack();
        };
        DuplicateResolve = vm;
        PushNav(CurrentView);
        CurrentView = AppView.DuplicateResolve;
    }

    public async Task OpenSectionAsync(long sectionId)
    {
        await SectionDetail.LoadAsync(sectionId);
        PushNav(CurrentView);
        CurrentView = AppView.SectionDetail;
    }

    public async Task OpenRenameToolAsync(SeriesViewModel series)
    {
        await RenameTool.LoadAsync(series.SeriesId, series.BaseTitle, series.IsStandalone);
        PushNav(CurrentView);
        CurrentView = AppView.RenameTool;
    }

    /// <summary>
    /// Opens the multi-series template rename tool seeded with the supplied series ids.
    /// No-op when <see cref="MultiRename"/> is null (e.g. in slim test contexts).
    /// </summary>
    public async Task OpenMultiRenameAsync(IReadOnlyList<long> seriesIds)
    {
        if (MultiRename is null || seriesIds.Count == 0) return;
        await MultiRename.LoadAsync(seriesIds, MultiRenameViewModel.DefaultTemplate);
        PushNav(CurrentView);
        CurrentView = AppView.MultiRename;
    }

    /// <summary>Picks a random unwatched episode and plays it. No-op when nothing is unwatched.</summary>
    [RelayCommand]
    private void SurpriseMe()
    {
        var ep = _libraryRepo.GetRandomUnwatchedEpisode();
        if (ep is not null) PlayEpisode(ep);
    }

    [RelayCommand]
    private void TogglePictureInPicture() => IsPictureInPicture = !IsPictureInPicture;

    [RelayCommand]
    private void ClosePlayer()
    {
        _player.FlushResume();
        _player.Engine.Stop();
        _player.IsFullscreen = false;
        IsPlayerVisible = false;
        IsPictureInPicture = false;
    }

    /// <summary>Opens the Ctrl+K command palette overlay and resets its state.</summary>
    [RelayCommand]
    private void OpenCommandPalette()
    {
        if (CommandPalette is null) return;
        if (IsCommandPaletteOpen) return; // guard double-open
        CommandPalette.Reset();
        IsCommandPaletteOpen = true;
        // Focus request is handled by a code-behind hook on IsCommandPaletteOpen.
        OnPropertyChanged(nameof(IsCommandPaletteOpen));
    }

    /// <summary>
    /// Builds the static action registry that the CommandPaletteViewModel uses.
    /// Called once during DI construction after all commands are wired.
    /// </summary>
    public IReadOnlyList<(string Label, string Icon, Action Execute)> BuildActionRegistry()
        => new List<(string, string, Action)>
        {
            ("Home",         "Home24",     () => ShowHomeCommand.Execute(null)),
            ("Browse",       "Apps24",     () => ShowBrowseCommand.Execute(null)),
            ("Settings",     "Settings24", () => ShowSettingsCommand.Execute(null)),
            ("Smart Views",  "Library24",  () => ShowSmartViewsCommand.Execute(null)),
            ("Playlists",    "List24",     () => ShowPlaylistsCommand.Execute(null)),
            ("Watch Later",  "Heart24",    () => ShowWatchlistCommand.Execute(null)),
            ("Favorites",    "Heart24",    () => ShowFavoritesCommand.Execute(null)),
            ("History",      "Eye24",      () => ShowHistoryCommand.Execute(null)),
            ("Up Next / Queue", "List24",  () => ShowQueueCommand.Execute(null)),
            ("Surprise Me",  "Play24",     () => SurpriseMeCommand.Execute(null)),
            ("Scan Library", "ArrowReset24", () => ScanAndReloadCommand.Execute(null)),
        };

    /// <summary>Loads sources + library once at startup.</summary>
    public async Task InitializeAsync()
    {
        Sources.Load();
        OnPropertyChanged(nameof(IsLibraryEmpty));
        await Library.LoadSectionsAsync();
        await Discovery.LoadAsync();
        await Creators.LoadAsync(CancellationToken.None);
        CurrentView = AppView.Home;
    }

    [RelayCommand]
    private async Task ScanAndReload()
    {
        IsScanning = true;
        try
        {
            await _scanCoordinator.ScanAllAsync(CancellationToken.None);
            await _backfill.BackfillAsync(CancellationToken.None);
            if (_resolutionBackfill is not null)
                await _resolutionBackfill.BackfillAsync(CancellationToken.None);
            Sources.Load();
            await Library.LoadSectionsAsync();
            await Discovery.LoadAsync();
            await Creators.LoadAsync(CancellationToken.None);
            Settings.MarkScanned();
            OnPropertyChanged(nameof(IsLibraryEmpty));
        }
        finally
        {
            IsScanning = false;
        }
    }
}
