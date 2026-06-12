using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Shapes;
using System.Windows.Media;
using System.Windows.Threading;
using VideoShelf.App.Services;
using VideoShelf.App.ViewModels;

namespace VideoShelf.App.Views;

public partial class PlayerView : UserControl
{
    private readonly DispatcherTimer _autoHide;
    private MainViewModel? _main;

    public PlayerView()
    {
        InitializeComponent();
        _autoHide = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _autoHide.Tick += OnAutoHideTick;
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
        RenderChapterTicks();
        ShowControls();
        Focus();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _autoHide.Stop();
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
    }

    private void OnAutoHideTick(object? sender, EventArgs e)
    {
        _autoHide.Stop();
        if (_main is null) return;
        if (_main.Player.AutoHideSuppressed || !_main.Player.IsPlaying ||
            _main.Player.IsScrubbing || _main.Player.HasError || _main.Player.CanResume)
            return;
        _main.Player.AreControlsVisible = false;
    }

    private void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PlayerViewModel.LengthSeconds) or nameof(PlayerViewModel.HasChapters))
            RenderChapterTicks();
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
        SeekPreview.Margin = new Thickness(Math.Max(0, x), 0, 0, 86);
    }

    private void RenderChapterTicks()
    {
        ChapterTicks.Children.Clear();
        if (_main is null) return;
        var length = _main.Player.LengthSeconds;
        if (length <= 0 || SeekBar.ActualWidth <= 0) return;
        foreach (var ch in _main.Player.Chapters)
        {
            if (ch.StartSeconds <= 0 || ch.StartSeconds >= length) continue;
            var x = (ch.StartSeconds / length) * SeekBar.ActualWidth;
            var tick = new Rectangle { Width = 2, Height = 6, Fill = Brushes.White, Opacity = 0.7 };
            Canvas.SetLeft(tick, x);
            ChapterTicks.Children.Add(tick);
        }
    }

    private void ApplyPipState()
    {
        if (_main is null) return;
        var pip = _main.IsPictureInPicture;
        BackToWindowButton.Visibility = pip ? Visibility.Visible : Visibility.Collapsed;
        if (pip)
        {
            PipTranslate.X = Math.Max(0, ActualWidth - 360 - 24);
            PipTranslate.Y = Math.Max(0, ActualHeight - 203 - 24);
        }
        else
        {
            PipTranslate.X = 0; PipTranslate.Y = 0;
        }
        ShowControls();
        Dispatcher.BeginInvoke(new Action(RenderChapterTicks), DispatcherPriority.Loaded);
    }

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
        if (!_dragging) return;
        var p = e.GetPosition(this);
        var dx = p.X - _dragStart.X;
        var dy = p.Y - _dragStart.Y;
        _dragStart = p;
        PipTranslate.X = Math.Clamp(PipTranslate.X + dx, 0, Math.Max(0, ActualWidth - 360));
        PipTranslate.Y = Math.Clamp(PipTranslate.Y + dy, 0, Math.Max(0, ActualHeight - 203));
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
            case PlayerCommand.TogglePlayPause: main.Player.TogglePlayPauseCommand.Execute(null); e.Handled = true; break;
            case PlayerCommand.SeekForward: main.Player.Engine.SeekTo(main.Player.PositionSeconds + 10); e.Handled = true; break;
            case PlayerCommand.SeekBackward: main.Player.Engine.SeekTo(main.Player.PositionSeconds - 10); e.Handled = true; break;
            case PlayerCommand.ToggleFullscreen: main.Player.ToggleFullscreenCommand.Execute(null); e.Handled = true; break;
            case PlayerCommand.ExitFullscreen: main.Player.IsFullscreen = false; e.Handled = true; break;
            case PlayerCommand.Screenshot: main.Player.ScreenshotCommand.Execute(null); e.Handled = true; break;
        }
    }
}
