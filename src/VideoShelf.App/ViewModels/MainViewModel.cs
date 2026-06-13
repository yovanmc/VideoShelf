using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoShelf.App.Services;
using VideoShelf.App.ViewModels.Discovery;
using VideoShelf.Core.Models;

namespace VideoShelf.App.ViewModels;

public enum AppView { Home, Browse, SectionDetail, RenameTool, Search, Settings }

public sealed partial class MainViewModel : ObservableObject
{
    private readonly SourcesViewModel _sources;
    private readonly LibraryViewModel _library;
    private readonly IScanCoordinator _scanCoordinator;
    private readonly PlayerViewModel _player;
    private readonly SettingsViewModel _settings;
    private readonly MediaBackfillService _backfill;
    private readonly PlayQueueViewModel _playQueue;

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
        PlayQueueViewModel playQueue)
    {
        _sources = sources;
        _library = library;
        _scanCoordinator = scanCoordinator;
        _player = player;
        _settings = settings;
        _backfill = backfill;
        _playQueue = playQueue;

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
        Sources.Sources.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsLibraryEmpty));
    }

    public string Title => "VideoShelf";

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

    [ObservableProperty]
    private AppView _currentView = AppView.Home;

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
