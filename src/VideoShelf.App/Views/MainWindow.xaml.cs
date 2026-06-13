using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Wpf.Ui.Controls;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Search;

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

        // PreviewKeyDown fallback: fires even when a TextBox (persistent search box) has focus.
        // The Window.InputBindings Ctrl+K binding may be absorbed by focussed controls —
        // this guarantees Ctrl+K always reaches the palette open command.
        PreviewKeyDown += OnWindowPreviewKeyDown;

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

        // Focus the palette search TextBox whenever the palette opens.
        if (e.PropertyName == nameof(MainViewModel.IsCommandPaletteOpen) &&
            _viewModel.IsCommandPaletteOpen)
        {
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
            {
                PaletteSearchBox?.Focus();
            });
        }
    }

    /// <summary>
    /// PreviewKeyDown fallback for Ctrl+K: fires at the window level BEFORE any focused control
    /// (including the persistent search TextBox) processes the key. This guarantees Ctrl+K opens
    /// the palette even when a TextBox has keyboard focus (which would suppress Window.InputBindings).
    /// </summary>
    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (_viewModel.OpenCommandPaletteCommand.CanExecute(null))
            {
                _viewModel.OpenCommandPaletteCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    /// <summary>Double-clicking a palette row executes it.</summary>
    internal void PaletteItem_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem { DataContext: PaletteItemViewModel item } &&
            _viewModel.CommandPalette is { } palette)
        {
            palette.ExecuteItemCommand.Execute(item);
        }
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
