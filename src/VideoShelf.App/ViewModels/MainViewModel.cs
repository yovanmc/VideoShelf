using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoShelf.App.Services;
using VideoShelf.App.ViewModels.Discovery;
using VideoShelf.Core.Models;

namespace VideoShelf.App.ViewModels;

public enum AppView { Home, Browse, SectionDetail, RenameTool }

public sealed partial class MainViewModel : ObservableObject
{
    private readonly SourcesViewModel _sources;
    private readonly LibraryViewModel _library;
    private readonly IScanCoordinator _scanCoordinator;
    private readonly PlayerViewModel _player;
    private readonly SettingsViewModel _settings;

    public MainViewModel(
        SourcesViewModel sources,
        LibraryViewModel library,
        IScanCoordinator scanCoordinator,
        PlayerViewModel player,
        SettingsViewModel settings,
        DiscoveryViewModel discovery,
        SectionDetailViewModel sectionDetail,
        RenameToolViewModel renameTool,
        CreatorsViewModel creators)
    {
        _sources = sources;
        _library = library;
        _scanCoordinator = scanCoordinator;
        _player = player;
        _settings = settings;

        _library.PlayRequested += (_, ep) => PlayEpisode(ep);
        _player.NextEpisodeRequested += (_, ep) => PlayEpisode(ep);

        Discovery = discovery;
        SectionDetail = sectionDetail;
        RenameTool = renameTool;
        Creators = creators;
        Discovery.PlayRequested += (_, e) => PlayEpisode(e);
        Discovery.SectionOpenRequested += async (_, id) => await OpenSectionAsync(id);
        SectionDetail.PlayRequested += (_, e) => PlayEpisode(e);
        SectionDetail.RenameRequested += async (_, s) => await OpenRenameToolAsync(s);
        RenameTool.CloseRequested += (_, _) => CurrentView = AppView.SectionDetail;
        Creators.OpenCreatorRequested += async id => await OpenSectionAsync(id);
    }

    public string Title => "VideoShelf";

    public SourcesViewModel Sources => _sources;
    public LibraryViewModel Library => _library;
    public PlayerViewModel Player => _player;
    public SettingsViewModel Settings => _settings;
    public DiscoveryViewModel Discovery { get; }
    public SectionDetailViewModel SectionDetail { get; }
    public RenameToolViewModel RenameTool { get; }
    public CreatorsViewModel Creators { get; }

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

    /// <summary>Routes a play request into the player and shows the player pane.</summary>
    public void PlayEpisode(EpisodeView episode)
    {
        IsPlayerVisible = true;
        _player.Open(episode);
    }

    [RelayCommand]
    private void ShowHome() => CurrentView = AppView.Home;

    [RelayCommand]
    private void ShowBrowse() => CurrentView = AppView.Browse;

    public async Task OpenSectionAsync(long sectionId)
    {
        await SectionDetail.LoadAsync(sectionId);
        CurrentView = AppView.SectionDetail;
    }

    public async Task OpenRenameToolAsync(SeriesViewModel series)
    {
        await RenameTool.LoadAsync(series.SeriesId, series.BaseTitle, series.IsStandalone);
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
            Sources.Load();
            await Library.LoadSectionsAsync();
            await Discovery.LoadAsync();
            await Creators.LoadAsync(CancellationToken.None);
        }
        finally
        {
            IsScanning = false;
        }
    }
}
