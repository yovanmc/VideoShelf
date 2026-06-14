using System;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoShelf.Core.Models;

namespace VideoShelf.App.ViewModels;

/// <summary>
/// End-of-video Up-Next countdown card state machine.
///
/// Design: a gate inserted in front of MainViewModel.OpenPlayer — the single-next-decider
/// (MainViewModel.PlaybackEnded → GetNextAfterEnd) is UNCHANGED. When a next item exists,
/// MainViewModel calls ShowUpNext(next, openCallback) instead of calling OpenPlayer directly.
/// The openCallback IS the existing OpenPlayer call, so the single-funnel is preserved.
///
/// The countdown logic is exposed as TickCountdown() so unit tests can drive it without a
/// real DispatcherTimer (no Application.Current in xUnit). The DispatcherTimer is only
/// created when Application.Current is non-null (i.e., running inside a real WPF app).
/// </summary>
public sealed partial class UpNextViewModel : ObservableObject
{
    private const int DefaultCountdownSeconds = 10;

    private Action? _openNextCallback;
    private DispatcherTimer? _timer;

    // ── observable state ──────────────────────────────────────────────────

    /// <summary>True when the Up-Next card overlay should be visible.</summary>
    [ObservableProperty]
    private bool _isUpNextVisible;

    /// <summary>Title of the next episode to play.</summary>
    [ObservableProperty]
    private string _upNextTitle = string.Empty;

    /// <summary>
    /// Path to the thumbnail for the next episode. Due to the known limitation
    /// (RecencyCardViewModel.ThumbnailPath is never populated — video_art persists
    /// but ThumbnailPath is not wired on cards), this is null in practice and the
    /// UI falls back to the placeholder fill (ThumbPlaceholderBrush). No overclaiming.
    /// </summary>
    [ObservableProperty]
    private string? _upNextThumbnailPath;

    /// <summary>Seconds remaining in the countdown (10 → 0). Shown as a text number in the card.</summary>
    [ObservableProperty]
    private int _countdownSeconds;

    // ── public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Shows the Up-Next card and starts the countdown.
    /// <paramref name="next"/> supplies title + thumbnail path (thumbnail may be null).
    /// <paramref name="openNext"/> is the callback that will be called to open the next
    /// episode — it must be the SAME OpenPlayer funnel used everywhere else in MainViewModel.
    /// </summary>
    public void ShowUpNext(EpisodeView next, Action openNext)
    {
        // Cancel any existing countdown (e.g. queued items arriving fast).
        CancelTimer();

        _openNextCallback = openNext;
        UpNextTitle = next.Title;
        UpNextThumbnailPath = null;   // thumbnail not wired; placeholder shown by XAML
        CountdownSeconds = DefaultCountdownSeconds;
        IsUpNextVisible = true;

        // Only wire the real timer when running inside a WPF application.
        if (System.Windows.Application.Current is not null)
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (_, _) => TickCountdown();
            _timer.Start();
        }
    }

    /// <summary>
    /// Decrements the countdown by one tick and fires the open callback when it reaches 0.
    /// Called by the DispatcherTimer each second in production; called directly by unit tests.
    /// </summary>
    public void TickCountdown()
    {
        if (!IsUpNextVisible) return;

        CountdownSeconds--;
        if (CountdownSeconds <= 0)
        {
            FireOpenNext();
        }
    }

    // ── relay commands (bound from XAML card buttons) ─────────────────────

    /// <summary>Play now: fires the open callback immediately and hides the card.</summary>
    [RelayCommand]
    private void PlayNextNow() => FireOpenNext();

    /// <summary>Dismiss: cancels the countdown and hides the card without playing.</summary>
    [RelayCommand]
    private void DismissUpNext()
    {
        CancelTimer();
        _openNextCallback = null;
        IsUpNextVisible = false;
    }

    // ── internals ─────────────────────────────────────────────────────────

    private void FireOpenNext()
    {
        CancelTimer();
        IsUpNextVisible = false;
        var cb = _openNextCallback;
        _openNextCallback = null;   // clear before invoke to prevent double-fire
        cb?.Invoke();
    }

    private void CancelTimer()
    {
        if (_timer is { } t)
        {
            t.Stop();
            _timer = null;
        }
    }
}
