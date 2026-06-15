using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;
using VideoShelf.App.Motion;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Search;

namespace VideoShelf.App.Views;

public partial class MainWindow : FluentWindow
{
    private readonly MainViewModel _viewModel;
    private PlayerView? _playerView;

    public MainWindow(MainViewModel viewModel, IMotionPolicy? motionPolicy = null)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        // D1: wire the static ShouldAnimate gate from the injected IMotionPolicy.
        if (motionPolicy is not null)
            ViewTransition.ShouldAnimate = () => motionPolicy.ShouldAnimate;

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

            // H2 — "Rename…" button: resolve selected series ids from the creator grid and open MultiRename.
            // Scoped to Browse (creator-grid) only: other pages produce video selections, not whole-series selections.
            BulkActionBarHost.RenameRequested += async (_, _) =>
            {
                if (_viewModel.CurrentView == AppView.Browse)
                {
                    var seriesIds = _viewModel.Creators.GetSelectedSeriesIds();
                    if (seriesIds.Count > 0)
                        await _viewModel.OpenMultiRenameAsync(seriesIds);
                }
            };
        }

        // G1 — A–Z jump strip: scroll the creator grid to the first matching creator.
        _viewModel.Creators.JumpToLetterRequested += OnJumpToLetter;

        Loaded += async (_, _) =>
        {
            try { await _viewModel.InitializeAsync(); }
            catch { /* startup load is best-effort; surfaced via empty UI */ }
        };
    }

    /// <summary>
    /// G1 — Handles a jump-strip letter click.
    /// Finds the first creator whose name starts with the requested letter and
    /// calls <see cref="ListBox.ScrollIntoView(object)"/> on the active grid ListBox.
    /// ScrollIntoView is the recommended reliable path for VirtualizingWrapPanel;
    /// BringIndexIntoView is a fallback in Group I's sweep if the visual scroll is absent.
    /// </summary>
    private void OnJumpToLetter(object? sender, JumpLetterArgs e)
    {
        // Collect the names that are currently visible (the collection view may filter them).
        // We scroll based on the first matching item in Creators (the underlying collection),
        // which mirrors what the ListBox renders (filter may hide some, but scroll still works
        // for the unfiltered default case; filtered-list jump is acceptable best-effort).
        var creators = _viewModel.Creators.Creators;
        var names = new List<string>(creators.Count);
        foreach (var c in creators) names.Add(c.Name);

        var idx = JumpListIndex.FirstIndexForLetter(names, e.Letter);
        if (idx < 0 || idx >= creators.Count) return;

        // The active ListBox depends on the current view mode.
        var listBox = CreatorsGridListBox;   // both grid and list views use the same creator collection
        listBox.ScrollIntoView(creators[idx]);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsPlayerVisible))
            UpdatePlayerHost(_viewModel.IsPlayerVisible);

        // D6: animate PiP host width/height on snap-to-corner.
        if (e.PropertyName == nameof(MainViewModel.IsPictureInPicture))
            UpdatePiPAnimation(_viewModel.IsPictureInPicture);

    }

    /// <summary>
    /// PiP snap: release any animation holds so the DataTrigger Setters (or the
    /// default Stretch layout) provide the correct static size. No BeginAnimation
    /// flourish — layout transitions via DataTrigger are sufficient.
    /// </summary>
    private void UpdatePiPAnimation(bool pipOn)
    {
        // Release any layout animation holds so the DataTrigger Setters take over.
        PlayerHost.BeginAnimation(WidthProperty, null);
        PlayerHost.BeginAnimation(HeightProperty, null);
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

    /// <summary>
    /// Harness hook: returns the live <see cref="PlayerView"/> instance (non-null only while
    /// <see cref="MainViewModel.IsPlayerVisible"/> is true). Used by <see cref="HarnessRunner"/>
    /// to open flyouts that are view-level Popups with no VM binding.
    /// </summary>
    internal PlayerView? GetPlayerView() => _playerView;

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
