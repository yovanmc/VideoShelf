using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoShelf.App.Motion;
using VideoShelf.App.Services;
using VideoShelf.App.ViewModels.Discovery;
using VideoShelf.Core.Models;

namespace VideoShelf.App.ViewModels;

public enum AppView { Home, Browse, SectionDetail, RenameTool, Search, Settings, Queue, Favorites, WatchLater, Playlists, History, Maintenance, DuplicateResolve, Insights }

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
    private readonly IToastService _toasts;
    private readonly IMotionPolicy? _motion;

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
        FavoritesViewModel favorites,
        WatchLaterViewModel watchlist,
        PlaylistsViewModel playlists,
        HistoryViewModel history,
        VideoShelf.Core.Storage.LibraryRepository libraryRepo,
        BulkActionBarViewModel? bulkBar = null,
        ResolutionBackfillService? resolutionBackfill = null,
        MaintenanceViewModel? maintenance = null,
        IToastService? toasts = null,
        IMotionPolicy? motion = null,
        InsightsViewModel? insights = null)
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
        _toasts = toasts ?? new ToastService((_, _) => { }); // no-op timer in test contexts
        _motion = motion;
        Maintenance = maintenance;
        Insights = insights;
        Favorites = favorites;
        Watchlist = watchlist;
        Playlists = playlists;
        History = history;
        BulkBar = bulkBar;

        _library.PlayRequested += (_, ep) => PlayEpisode(ep);

        _playQueue.PlayRequested += (_, ep) => OpenPlayer(ep);
        _player.PlaybackEnded += (_, finished) =>
        {
            // Single next-decider invariant (M14): GetNextAfterEnd is the sole source of
            // truth for what plays next. The Up-Next countdown is a GATE inserted in front
            // of the existing OpenPlayer call — the decider logic is untouched.
            var next = _playQueue.GetNextAfterEnd(finished);
            if (next is not null)
                UpNext.ShowUpNext(next, () => OpenPlayer(next));
            // When next == null, existing end behavior is preserved (nothing happens).
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
        SectionDetail.EpisodeRenameRequested += async (_, t) => await OpenRenameToolAsync(t.Series, t.VideoId);
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

        // E2: After undo-remove re-adds a source, trigger a full scan+reload so the library
        // reflects the restored source without requiring a manual "Scan" press.
        Sources.OnSourceRestored = () => _ = ScanAndReloadCommand.ExecuteAsync(null);
    }

    /// <summary>Up-Next countdown card state machine; exposed so PlayerView can bind to it.</summary>
    public UpNextViewModel UpNext { get; } = new();

    /// <summary>Toast service; exposed so the ToastHost overlay can bind to Toasts.Toasts
    /// and so B4 harness can call Show() directly.</summary>
    public IToastService Toasts => _toasts;

    /// <summary>
    /// True when UI animations should play. Reads <see cref="IMotionPolicy.ShouldAnimate"/>;
    /// falls back to true (animate) when no policy is injected (test contexts).
    /// Bind skeleton-panel Animate, shimmer Storyboard gates, etc. to this property.
    /// </summary>
    public bool AnimationsEnabled => _motion?.ShouldAnimate ?? true;

    /// <summary>
    /// Human-readable scan phase label shown next to the progress ring while <see cref="IsScanning"/>
    /// is true.  Updated at each phase transition ("Scanning library…" → "Probing durations…").
    /// NOTE: no real incremental file-count callback exists in the scan pipeline; only honest
    /// phase labels are surfaced here (C2 honesty gate — no fabricated numbers).
    /// </summary>
    [ObservableProperty]
    private string _scanStatusText = string.Empty;

    public string Title => "VideoShelf";

    /// <summary>Static window title.</summary>
    public string WindowTitle => "VideoShelf";

    /// <summary>Bulk-action bar; null in test contexts that don't supply it (nullable-trailing-param pattern).</summary>
    public BulkActionBarViewModel? BulkBar { get; }

    public FavoritesViewModel Favorites { get; }
    public WatchLaterViewModel Watchlist { get; }
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

    /// <summary>Insights dashboard VM; null in test contexts that don't supply it (nullable-trailing-param pattern).</summary>
    public InsightsViewModel? Insights { get; }

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
            AppView.WatchLater   => Watchlist,
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

    partial void OnIsPlayerVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(IsInlinePlayerVisible));
    }
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
        // ResumePositionSeconds is set synchronously inside Open() from the DB; read it immediately after.
        // Resume toast: raised from MainViewModel so IToastService stays out of PlayerViewModel.
        // ResumePositionSeconds is set synchronously in Open() from the DB. CanResume is set later
        // in OnLengthChanged (async engine event). We use ResumePositionSeconds > 0 as the signal
        // so the toast fires immediately on open when a saved position exists, before the length arrives.
        var savedPosition = _player.ResumePositionSeconds;
        if (savedPosition > 0)
        {
            var position = System.TimeSpan.FromSeconds(savedPosition);
            _toasts.Show($"Resumed at {position:hh\\:mm\\:ss}");
        }
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
    private void ShowFavorites()
    {
        Favorites.Load();
        PushNav(CurrentView);
        CurrentView = AppView.Favorites;
    }

    [RelayCommand]
    private void ShowWatchLater()
    {
        Watchlist.Load();
        PushNav(CurrentView);
        CurrentView = AppView.WatchLater;
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
        // G4: Refresh the badge after Maintenance loads (counts are now up-to-date).
        if (Maintenance is not null)
            LibraryIssueCount = Maintenance.MissingCount + Maintenance.DuplicateGroupCount;
    }

    [RelayCommand]
    private void ShowInsights()
    {
        Insights?.Load();
        PushNav(CurrentView);
        CurrentView = AppView.Insights;
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

    public async Task OpenRenameToolAsync(SeriesViewModel series, long? videoId = null)
    {
        await RenameTool.LoadAsync(series.SeriesId, series.BaseTitle, series.IsStandalone, videoId);
        PushNav(CurrentView);
        CurrentView = AppView.RenameTool;
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

    // ── G4: Library-Health issue badge ────────────────────────────────────────

    /// <summary>
    /// Count of outstanding library health issues (missing files + duplicate groups).
    /// Populated cheaply from the DB at startup via a lightweight summary query — no scan.
    /// Updated each time the Maintenance page is opened (<see cref="ShowMaintenance"/>).
    /// 0 means no issues (or not yet queried).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLibraryIssues))]
    private int _libraryIssueCount;

    /// <summary>True when there are outstanding library health issues — drives the badge visibility.</summary>
    public bool HasLibraryIssues => LibraryIssueCount > 0;

    /// <summary>Refreshes the library issue badge count from the DB (cheap query, no scan).</summary>
    private void RefreshLibraryIssueBadge()
    {
        if (Maintenance is null) return;
        // Re-use the summary data if Maintenance has already loaded; otherwise do a lightweight query.
        if (Maintenance.MissingCount > 0 || Maintenance.DuplicateGroupCount > 0)
        {
            LibraryIssueCount = Maintenance.MissingCount + Maintenance.DuplicateGroupCount;
        }
        else
        {
            // Lightweight DB-only summary (cheap — no filesystem scan).
            Maintenance.Load();
            LibraryIssueCount = Maintenance.MissingCount + Maintenance.DuplicateGroupCount;
        }
    }

    /// <summary>Loads sources + library once at startup.</summary>
    public async Task InitializeAsync()
    {
        Sources.Load();
        OnPropertyChanged(nameof(IsLibraryEmpty));
        await Library.LoadSectionsAsync();
        await Discovery.LoadAsync();
        await Creators.LoadAsync(CancellationToken.None);
        CurrentView = AppView.Home;
        // G4: Populate the Library-Health badge from a cheap DB query (no scan triggered).
        RefreshLibraryIssueBadge();
    }

    [RelayCommand]
    private async Task ScanAndReload()
    {
        IsScanning = true;
        ScanStatusText = "Scanning library…";
        try
        {
            var result = await _scanCoordinator.ScanAllAsync(CancellationToken.None);

            ScanStatusText = "Probing durations…";
            await _backfill.BackfillAsync(CancellationToken.None);
            if (_resolutionBackfill is not null)
                await _resolutionBackfill.BackfillAsync(CancellationToken.None);

            Sources.Load();
            await Library.LoadSectionsAsync();
            await Discovery.LoadAsync();
            await Creators.LoadAsync(CancellationToken.None);

            var summary = FormatScanSummary(result);
            ScanStatusText = summary;
            Settings.MarkScanned(summary);
            Maintenance?.SetScanSummary(summary);

            OnPropertyChanged(nameof(IsLibraryEmpty));
        }
        finally
        {
            IsScanning = false;
            // Leave ScanStatusText as the final summary (or clear it after a brief delay — keep
            // the summary visible so the user can read the result; MarkScanned persists it anyway).
        }
    }

    /// <summary>
    /// Formats a <see cref="VideoShelf.Core.Scanning.ScanResult"/> as a human-readable diff string,
    /// e.g. "Added 12 · updated 3 · restored 1 · missing 1".
    /// </summary>
    internal static string FormatScanSummary(VideoShelf.Core.Scanning.ScanResult result)
        => $"Added {result.Added} · updated {result.Updated} · restored {result.Restored} · missing {result.Missing}";
}
