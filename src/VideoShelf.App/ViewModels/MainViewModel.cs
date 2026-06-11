using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoShelf.App.Services;
using VideoShelf.Core.Models;

namespace VideoShelf.App.ViewModels;

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
        SettingsViewModel settings)
    {
        _sources = sources;
        _library = library;
        _scanCoordinator = scanCoordinator;
        _player = player;
        _settings = settings;

        _library.PlayRequested += (_, ep) => PlayEpisode(ep);
        _player.NextEpisodeRequested += (_, ep) => PlayEpisode(ep);
    }

    public string Title => "VideoShelf";

    public SourcesViewModel Sources => _sources;
    public LibraryViewModel Library => _library;
    public PlayerViewModel Player => _player;
    public SettingsViewModel Settings => _settings;

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
        }
        finally
        {
            IsScanning = false;
        }
    }
}
