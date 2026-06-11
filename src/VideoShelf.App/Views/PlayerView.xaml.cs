using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VideoShelf.App.Services;
using VideoShelf.App.ViewModels;

namespace VideoShelf.App.Views;

public partial class PlayerView : UserControl
{
    public PlayerView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        KeyDown += OnKeyDown;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachSurface();

        // Refresh live tracks/chapters shortly after media starts.
        if (DataContext is MainViewModel m)
            m.Player.RefreshTracks();

        Focus();
    }

    /// <summary>Binds the shared libVLC MediaPlayer to the inline VideoView. Safe to call repeatedly;
    /// used to re-host the surface after returning from the detached mini-player.</summary>
    public void AttachSurface()
    {
        if (DataContext is MainViewModel main &&
            main.Player.Engine is LibVlcPlaybackEngine vlc)
        {
            VideoSurface.MediaPlayer = vlc.MediaPlayer;
        }
    }

    /// <summary>Releases the MediaPlayer from the inline VideoView so another VideoView (the mini-player)
    /// can host it — a libVLC MediaPlayer may be hosted by only one VideoView at a time.</summary>
    public void DetachSurface()
    {
        VideoSurface.MediaPlayer = null;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel main)
            return;

        var command = PlayerKeyMap.Resolve(e.Key, Keyboard.Modifiers);
        switch (command)
        {
            case PlayerCommand.TogglePlayPause: main.Player.TogglePlayPauseCommand.Execute(null); e.Handled = true; break;
            case PlayerCommand.SeekForward: main.Player.Engine.SeekTo(main.Player.PositionSeconds + 10); e.Handled = true; break;
            case PlayerCommand.SeekBackward: main.Player.Engine.SeekTo(main.Player.PositionSeconds - 10); e.Handled = true; break;
            case PlayerCommand.ToggleFullscreen: main.Player.ToggleFullscreenCommand.Execute(null); e.Handled = true; break;
            case PlayerCommand.ExitFullscreen: main.Player.IsFullscreen = false; e.Handled = true; break;
            case PlayerCommand.Screenshot: main.Player.ScreenshotCommand.Execute(null); e.Handled = true; break;
        }
    }
}
