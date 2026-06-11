using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;
using VideoShelf.App.Services;
using VideoShelf.App.ViewModels;

namespace VideoShelf.App.Views;

public partial class MainWindow : FluentWindow
{
    private readonly MainViewModel _viewModel;
    private MiniPlayerWindow? _miniPlayer;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.Player.PropertyChanged += OnPlayerPropertyChanged;
        Loaded += async (_, _) =>
        {
            try { await _viewModel.InitializeAsync(); }
            catch { /* startup load is best-effort; surfaced via empty UI */ }
        };
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsPictureInPicture))
            UpdatePictureInPicture(_viewModel.IsPictureInPicture);
    }

    private void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerViewModel.IsFullscreen))
            UpdateFullscreen(_viewModel.Player.IsFullscreen);
    }

    /// <summary>Fullscreen collapses the title-bar chrome and maximizes the window. The inline player
    /// already overlays both columns, so this fills the screen. Uses only WindowState (no WindowStyle/
    /// transparency changes, which can throw on a FluentWindow); true borderless polish is a Phase 6 item.</summary>
    private void UpdateFullscreen(bool on)
    {
        if (on)
        {
            AppTitleBar.Visibility = Visibility.Collapsed;
            TitleBarRow.Height = new GridLength(0);
            WindowState = WindowState.Maximized;
        }
        else
        {
            AppTitleBar.Visibility = Visibility.Visible;
            TitleBarRow.Height = new GridLength(44);
            WindowState = WindowState.Normal;
        }
    }

    private void UpdatePictureInPicture(bool on)
    {
        if (_viewModel.Player.Engine is not LibVlcPlaybackEngine vlc)
            return;

        if (on)
        {
            // A MediaPlayer may be hosted by only one VideoView: free the inline surface
            // BEFORE the mini-player claims it, else the video blanks/freezes.
            InlinePlayer.DetachSurface();

            _miniPlayer = new MiniPlayerWindow(vlc.MediaPlayer);
            _miniPlayer.ReturnRequested += (_, _) => _viewModel.IsPictureInPicture = false;
            _miniPlayer.Closed += (_, _) =>
            {
                // Closing the mini window leaves PiP and returns the player inline.
                if (_viewModel.IsPictureInPicture)
                    _viewModel.IsPictureInPicture = false;
            };
            _miniPlayer.Show();
        }
        else if (_miniPlayer is not null)
        {
            _miniPlayer.DetachPlayer();   // release the MediaPlayer before the inline surface re-hosts it
            var w = _miniPlayer;
            _miniPlayer = null;
            w.Close();
            // Re-host the shared MediaPlayer on the inline surface now that PiP has cleared.
            InlinePlayer.AttachSurface();
        }
    }
}
