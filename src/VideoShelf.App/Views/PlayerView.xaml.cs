using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using VideoShelf.App.Services;
using VideoShelf.App.ViewModels;

namespace VideoShelf.App.Views;

public partial class PlayerView : UserControl
{
    private readonly DispatcherTimer _autoHide;
    private MainViewModel? _main;

    // Click-to-pause: single/double-click discriminator.
    // A short timer fires the pause only if no second click arrives within the window.
    // Group E's double-click-fullscreen handler should call _singleClickTimer.Stop() +
    // e.Handled = true on ClickCount == 2 before the timer fires.
    private readonly DispatcherTimer _singleClickTimer;
    private const double DragThresholdPx = 4.0;
    private Point _mouseDownPos;

    private FrameworkElement? Host => this.Parent as FrameworkElement;

    public PlayerView()
    {
        InitializeComponent();
        _autoHide = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _autoHide.Tick += OnAutoHideTick;

        _singleClickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        _singleClickTimer.Tick += OnSingleClickTimerTick;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        KeyDown += OnKeyDown;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachSurface();
        _main = DataContext as MainViewModel;
        if (_main is not null)
        {
            _main.Player.RefreshTracks();
            _main.Player.PropertyChanged += OnPlayerPropertyChanged;
            _main.PropertyChanged += OnMainPropertyChanged;
        }

        SeekBar.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(OnSeekDragStarted));
        SeekBar.AddHandler(Thumb.DragDeltaEvent, new DragDeltaEventHandler(OnSeekDragDelta));
        SeekBar.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(OnSeekDragCompleted));

        ApplyPipState();
        ShowControls();
        Focus();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _autoHide.Stop();
        _singleClickTimer.Stop();
        if (_main is not null)
        {
            _main.Player.PropertyChanged -= OnPlayerPropertyChanged;
            _main.PropertyChanged -= OnMainPropertyChanged;
        }
        DetachSurface();
    }

    /// <summary>Binds the shared libVLC MediaPlayer to the VideoView (one VideoView for full + PiP).</summary>
    public void AttachSurface()
    {
        if (DataContext is MainViewModel main && main.Player.Engine is LibVlcPlaybackEngine vlc)
            VideoSurface.MediaPlayer = vlc.MediaPlayer;
    }

    public void DetachSurface() => VideoSurface.MediaPlayer = null;

    private void OnSurfaceMouseMove(object sender, MouseEventArgs e) => ShowControls();

    private void ShowControls()
    {
        if (_main is not null) _main.Player.AreControlsVisible = true;
        _autoHide.Stop();
        _autoHide.Start();
        // Restore cursor when controls become visible
        Cursor = Cursors.Arrow;
    }

    private void OnAutoHideTick(object? sender, EventArgs e)
    {
        _autoHide.Stop();
        if (_main is null) return;
        if (_main.Player.AutoHideSuppressed || !_main.Player.IsPlaying ||
            _main.Player.IsScrubbing || _main.Player.HasError || _main.Player.CanResume)
            return;
        // Close any open flyouts before the controls layer collapses, so they don't
        // float as orphaned HWNDs above the video.
        VolumeFlyout.IsOpen = false;
        TracksFlyout.IsOpen = false;
        MoreFlyout.IsOpen = false;
        _main.Player.AreControlsVisible = false;
        // Also hide the mouse cursor over the video
        Cursor = Cursors.None;
    }

    private void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Restore cursor + controls when an error banner or resume offer appears so
        // the user can interact with them even if auto-hide already fired.
        if ((e.PropertyName == nameof(PlayerViewModel.HasError) && (_main?.Player.HasError ?? false)) ||
            (e.PropertyName == nameof(PlayerViewModel.CanResume) && (_main?.Player.CanResume ?? false)))
        {
            ShowControls();
        }
    }

    private void OnMainPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsPictureInPicture))
            ApplyPipState();
    }

    private void OnSeekDragStarted(object sender, DragStartedEventArgs e)
    {
        _main?.Player.BeginScrub();
        ShowControls();
    }

    private async void OnSeekDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (_main is null) return;
        _autoHide.Stop();
        PositionSeekPreview(_main.Player.ScrubPosition, _main.Player.LengthSeconds);
        await _main.Player.UpdateScrubPreviewAsync(_main.Player.ScrubPosition);
    }

    private void OnSeekDragCompleted(object sender, DragCompletedEventArgs e)
    {
        _main?.Player.CommitScrub();
        ShowControls();
    }

    private void PositionSeekPreview(double seconds, double length)
    {
        if (length <= 0 || SeekBar.ActualWidth <= 0) return;
        var frac = Math.Clamp(seconds / length, 0, 1);
        var x = SeekBar.TranslatePoint(new Point(0, 0), RootGrid).X
                + frac * SeekBar.ActualWidth - SeekPreview.Width / 2;
        SeekPreview.Margin = new Thickness(Math.Max(0, x), 0, 0, 96);
    }

    private void ApplyPipState()
    {
        if (_main is null) return;
        var pip = _main.IsPictureInPicture;
        BackToWindowButton.Visibility = pip ? Visibility.Visible : Visibility.Collapsed;
        var host = Host;
        if (host is not null)
        {
            if (pip)
            {
                var win = Window.GetWindow(this);
                var w = win?.ActualWidth ?? 1180;
                var h = win?.ActualHeight ?? 760;
                var left = Math.Max(0, w - 360 - 48);
                var top = Math.Max(0, h - 203 - 96);
                host.Margin = new Thickness(left, top, 0, 0);
            }
            else
            {
                host.Margin = new Thickness(0);
            }
        }
        ShowControls();
    }

    // ===== Popup flyout handlers (view concern) ================================

    private void OnVolumeButtonClick(object sender, RoutedEventArgs e)
    {
        TracksFlyout.IsOpen = false;
        MoreFlyout.IsOpen = false;
        VolumeFlyout.IsOpen = !VolumeFlyout.IsOpen;
    }

    private void OnTracksButtonClick(object sender, RoutedEventArgs e)
    {
        VolumeFlyout.IsOpen = false;
        MoreFlyout.IsOpen = false;
        TracksFlyout.IsOpen = !TracksFlyout.IsOpen;
    }

    private void OnMoreButtonClick(object sender, RoutedEventArgs e)
    {
        VolumeFlyout.IsOpen = false;
        TracksFlyout.IsOpen = false;
        MoreFlyout.IsOpen = !MoreFlyout.IsOpen;
    }

    // ===== Click-to-pause with single/double discriminator ====================
    // MouseDown records the position; MouseUp checks for click vs. drag,
    // then arms the single-click timer. The timer fires the pause after
    // 220 ms — long enough for a double-click (typically < 200 ms) to stop it.
    // Group E's double-click-fullscreen handler should set e.Handled = true and
    // call _singleClickTimer.Stop() when ClickCount == 2.

    private void OnVideoSurfaceMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Only handle clicks on the video surface itself, not on control buttons.
        // If the click hits a control in ControlsLayer the event is already Handled.
        if (e.Handled) return;

        // E2: double-click-fullscreen — fires here (ClickCount == 2 on the second MouseDown in a
        // double-click sequence). Stop the pending single-click timer so the pause toggle does NOT
        // fire as well, then toggle fullscreen.
        if (e.ClickCount == 2)
        {
            _singleClickTimer.Stop();
            _main?.Player.ToggleFullscreenCommand.Execute(null);
            e.Handled = true;
            return;
        }

        _mouseDownPos = e.GetPosition(this);
    }

    private void OnVideoSurfaceMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled) return;
        // Ignore if released far from where button went down (drag gesture).
        var upPos = e.GetPosition(this);
        var dx = upPos.X - _mouseDownPos.X;
        var dy = upPos.Y - _mouseDownPos.Y;
        if (Math.Sqrt(dx * dx + dy * dy) > DragThresholdPx) return;

        // Arm the single-click timer; it fires the pause unless a double-click cancels it.
        _singleClickTimer.Stop();
        _singleClickTimer.Start();
    }

    private void OnSingleClickTimerTick(object? sender, EventArgs e)
    {
        _singleClickTimer.Stop();
        _main?.Player.TogglePlayPauseCommand.Execute(null);
    }

    // ==========================================================================

    private Point _dragStart;
    private bool _dragging;

    private void OnTopBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_main is null || !_main.IsPictureInPicture) return;
        _dragging = true;
        _dragStart = e.GetPosition(this);
        ((UIElement)sender).CaptureMouse();
        ((UIElement)sender).MouseMove += OnTopBarMouseMove;
        ((UIElement)sender).MouseLeftButtonUp += OnTopBarMouseUp;
        e.Handled = true;
    }

    private void OnTopBarMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || Host is not FrameworkElement host) return;
        var p = e.GetPosition(this);
        var dx = p.X - _dragStart.X;
        var dy = p.Y - _dragStart.Y;
        _dragStart = p;
        var win = Window.GetWindow(this);
        var maxLeft = Math.Max(0, (win?.ActualWidth ?? 1180) - 360);
        var maxTop = Math.Max(0, (win?.ActualHeight ?? 760) - 203);
        var left = Math.Clamp(host.Margin.Left + dx, 0, maxLeft);
        var top = Math.Clamp(host.Margin.Top + dy, 0, maxTop);
        host.Margin = new Thickness(left, top, 0, 0);
    }

    private void OnTopBarMouseUp(object sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        ((UIElement)sender).ReleaseMouseCapture();
        ((UIElement)sender).MouseMove -= OnTopBarMouseMove;
        ((UIElement)sender).MouseLeftButtonUp -= OnTopBarMouseUp;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel main) return;
        ShowControls();
        var command = PlayerKeyMap.Resolve(e.Key, Keyboard.Modifiers);
        switch (command)
        {
            case PlayerCommand.TogglePlayPause:   main.Player.TogglePlayPauseCommand.Execute(null); e.Handled = true; break;
            // E3: route Left/Right through the VM skip commands so feedback fires + clamping is single-source
            case PlayerCommand.SkipBack:          main.Player.SkipBack10Command.Execute(null); e.Handled = true; break;
            case PlayerCommand.SkipForward:       main.Player.SkipForward30Command.Execute(null); e.Handled = true; break;
            case PlayerCommand.ToggleFullscreen:  main.Player.ToggleFullscreenCommand.Execute(null); e.Handled = true; break;
            case PlayerCommand.ExitFullscreen:    HandleEscapeKey(main); e.Handled = true; break;
        }
    }

    /// <summary>
    /// Esc back-out chain (B4):
    /// 1. Close any open flyout (More/Tracks/Volume) — swallows the Esc.
    /// 2. Exit fullscreen if the player is in fullscreen mode.
    /// 3. Close the player entirely and return focus to the launching card.
    /// Each level "consumes" the key and does not fall through to the next.
    /// </summary>
    private void HandleEscapeKey(MainViewModel main)
    {
        // Priority 1: close an open flyout first.
        if (MoreFlyout.IsOpen)   { MoreFlyout.IsOpen   = false; return; }
        if (TracksFlyout.IsOpen) { TracksFlyout.IsOpen = false; return; }
        if (VolumeFlyout.IsOpen) { VolumeFlyout.IsOpen = false; return; }

        // Priority 2: exit fullscreen.
        if (main.Player.IsFullscreen) { main.Player.IsFullscreen = false; return; }

        // Priority 3: close the player and restore focus to the launching card.
        main.ClosePlayerCommand.Execute(null);
    }

    // ===== E4: volume scroll feedback ==========================================

    private void OnVideoSurfaceMouseWheel(object sender, MouseWheelEventArgs e)
    {
        _main?.Player.AdjustVolumeByWheel(e.Delta);
        e.Handled = true;
    }

    // ===== Harness hook: open a named flyout from HarnessRunner ================
    // Additive, harness-only, theming-safe — no VM change, no binding side-effect.
    // Called after Loaded fires (harness waits for done-signal, which is written after
    // SettleAsync, so the PlayerView is guaranteed to be in the visual tree).

    /// <summary>
    /// Opens one of the view-level Popup flyouts by name for the visual sweep harness.
    /// Valid values: "Volume", "Tracks", "More".
    /// No-op for unknown names so the harness is forward-compatible.
    /// </summary>
    internal void OpenFlyoutForHarness(string which)
    {
        // Close all first (same mutual-exclusion logic as the button handlers)
        VolumeFlyout.IsOpen = false;
        TracksFlyout.IsOpen = false;
        MoreFlyout.IsOpen   = false;

        switch (which)
        {
            case "Volume": VolumeFlyout.IsOpen = true; break;
            case "Tracks": TracksFlyout.IsOpen = true; break;
            case "More":   MoreFlyout.IsOpen   = true; break;
        }
    }
}
