using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;
using VideoShelf.App.ViewModels;

namespace VideoShelf.App.Views;

public partial class MainWindow : FluentWindow
{
    private readonly MainViewModel _viewModel;
    private PlayerView? _playerView;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.Player.PropertyChanged += OnPlayerPropertyChanged;

        // C — generalized active-page bulk bar wiring.
        if (_viewModel.BulkBar is not null)
        {
            // ClearButton dismisses selection on whichever page is currently active.
            BulkActionBarHost.ClearRequested += (_, _) =>
            {
                _viewModel.ActiveSelectionSource?.ClearSelection();
            };

            // When the active selection source changes, push updated video ids to the bar.
            _viewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.BulkBarVisible) ||
                    e.PropertyName == nameof(MainViewModel.ActiveSelectionSource))
                {
                    var source = _viewModel.ActiveSelectionSource;
                    _viewModel.BulkBar.SetVideoIds(
                        source is not null ? source.GetSelectedVideoIds() : System.Array.Empty<long>());
                }
            };

            // When a bulk action completes, exit selection mode on the active page.
            _viewModel.BulkBar.Completed += (_, _) =>
            {
                _viewModel.ActiveSelectionSource?.ExitSelectionMode();
            };
        }

        Loaded += async (_, _) =>
        {
            try { await _viewModel.InitializeAsync(); }
            catch { /* startup load is best-effort; surfaced via empty UI */ }
        };
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsPlayerVisible))
            UpdatePlayerHost(_viewModel.IsPlayerVisible);
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

    /// <summary>Realizes the player (and its VideoView) only while playing; tearing it down on hide
    /// destroys the airspace overlay window that otherwise bleeds the transport bar onto other views.
    /// PiP is an in-window mode of the SAME PlayerView — no separate window, so the vout never re-hosts.</summary>
    private void UpdatePlayerHost(bool visible)
    {
        if (visible)
        {
            _playerView ??= new PlayerView { DataContext = _viewModel };
            if (PlayerHost.Content is null) PlayerHost.Content = _playerView;
        }
        else
        {
            PlayerHost.Content = null; // Unloaded -> DetachSurface + VideoView/overlay HWND destroyed
        }
    }
}
