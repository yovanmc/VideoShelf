using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoShelf.App.Services;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.ViewModels;

/// <summary>
/// Testable player logic: transport, resume save/offer. Depends only on IPlaybackEngine + Core repos,
/// so it is fully unit-testable with a FakePlaybackEngine. The View binds to it and hosts the VideoView.
/// </summary>
public sealed partial class PlayerViewModel(
    IPlaybackEngine engine,
    LibraryRepository library,
    WatchRepository watch,
    SettingsRepository settings,
    ResumePolicy resumePolicy,
    ISubtitleFilePicker subtitlePicker,
    ItemArtRepository? itemArt = null) : ObservableObject
{
    private EpisodeView? _current;
    private double _lastSavedAt;
    private double _length;

    public IPlaybackEngine Engine => engine;

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private string? _playbackError;

    /// <summary>True when a playback/missing-file error is set — bound by the error banner's visibility
    /// (the shared BoolToVisibility converter is bool-only, so it cannot key off the string directly).</summary>
    public bool HasError => !string.IsNullOrEmpty(PlaybackError);

    partial void OnPlaybackErrorChanged(string? value) => OnPropertyChanged(nameof(HasError));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PositionText))]
    private double _positionSeconds;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PositionText))]
    private double _lengthSeconds;

    /// <summary>Human-readable seek position for UIA ItemStatus (e.g. "5s of 120s").
    /// Changes whenever PositionSeconds or LengthSeconds changes.</summary>
    public string PositionText => $"{(int)PositionSeconds}s of {(int)LengthSeconds}s";

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private bool _canResume;

    [ObservableProperty]
    private double _resumePositionSeconds;

    public ObservableCollection<TrackOption> AudioTracks { get; } = [];
    public ObservableCollection<TrackOption> SubtitleTracks { get; } = [];

    public bool HasMultipleAudioTracks => AudioTracks.Count > 1;
    public bool HasSubtitleTracks => SubtitleTracks.Count > 1;

    public string? CurrentFilePath => _current?.FilePath;
    public bool CanAddSubtitle => _current is not null;

    [ObservableProperty]
    private TrackOption? _selectedAudioTrack;

    [ObservableProperty]
    private TrackOption? _selectedSubtitleTrack;

    /// <summary>The scrubber's bound value. Mirrors PositionSeconds during playback, but is user-driven
    /// while IsScrubbing (so dragging the thumb doesn't fight the per-second position updates).</summary>
    [ObservableProperty]
    private double _scrubPosition;

    [ObservableProperty]
    private bool _isScrubbing;

    /// <summary>Path to the current seek-preview frame (shown in the thumbnail popup while scrubbing); null = none.</summary>
    [ObservableProperty]
    private string? _seekPreviewPath;

    /// <summary>Drives the auto-hiding overlay's visibility. The View shows controls on activity and hides
    /// them after an idle delay while playing; both set this. Starts visible.</summary>
    [ObservableProperty]
    private bool _areControlsVisible = true;

    /// <summary>When true, the View's auto-hide timer is suppressed (controls stay up). Set by the harness
    /// so the screenshot sweep captures the transport; off in normal use.</summary>
    public bool AutoHideSuppressed { get; set; }

    private CancellationTokenSource? _previewCts;

    [ObservableProperty]
    private bool _isFullscreen;

    public int Volume
    {
        get => engine.Volume;
        set
        {
            if (engine.Volume == value) return;
            engine.Volume = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Folder screenshots are written to. Set by DI/host; defaults to a temp-safe value for tests.</summary>
    public string CaptureDirectory { get; set; } = System.IO.Path.GetTempPath();

    /// <summary>Folder seek-preview frames are cached in.</summary>
    public string SeekPreviewDirectory { get; set; } = System.IO.Path.GetTempPath();

    /// <summary>Folder cover snapshots are written to. Set by DI from AppPaths.CoversDirectory; NEVER a library path.</summary>
    public string CoversDirectory { get; set; } = System.IO.Path.GetTempPath();

    /// <summary>The video id of the currently loaded episode, or null if nothing is open.</summary>
    public long? CurrentVideoId => _current?.VideoId;

    [ObservableProperty]
    private string? _lastScreenshotPath;

    [RelayCommand]
    private void Screenshot()
    {
        try
        {
            Directory.CreateDirectory(CaptureDirectory);
            var name = $"capture_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png";
            var target = Path.Combine(CaptureDirectory, name);
            LastScreenshotPath = engine.TrySnapshot(target) ? target : null;
        }
        catch
        {
            LastScreenshotPath = null; // fail-safe: a screenshot must never crash playback
        }
    }

    /// <summary>True when a video is loaded and ItemArtRepository is available.</summary>
    public bool CanSetCover => itemArt is not null && _current is not null;

    /// <summary>Snapshots the current frame into CoversDirectory and stores it as the video's cover art.
    /// Fail-safe: any error is swallowed so playback is never interrupted.</summary>
    [RelayCommand]
    private void SetCoverFromFrame()
    {
        try
        {
            if (itemArt is null || _current is null) return;
            var videoId = _current.VideoId;
            Directory.CreateDirectory(CoversDirectory);
            var target = Path.Combine(CoversDirectory, $"cover_{videoId}.png");
            if (engine.TrySnapshot(target))
                itemArt.SetVideoArt(videoId, target);
        }
        catch
        {
            // fail-safe: must never crash playback
        }
    }

    /// <summary>Produces a seek-preview frame PNG for the given position, or null on failure (fail-safe).</summary>
    /// <remarks>The frame is position-accurate and cached per rounded second.</remarks>
    public async Task<string?> RequestSeekPreviewAsync(double seconds, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(SeekPreviewDirectory);
            var target = Path.Combine(SeekPreviewDirectory, $"preview_{(int)Math.Round(seconds)}.png");
            if (File.Exists(target) && new FileInfo(target).Length > 0) return target;

            var ok = await engine.TryGeneratePreviewFrameAsync(seconds, target, cancellationToken)
                .ConfigureAwait(false);
            return ok && File.Exists(target) && new FileInfo(target).Length > 0 ? target : null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch
        {
            return null; // fail-safe
        }
    }

    /// <summary>Begins a scrub gesture: freezes ScrubPosition from playback updates.</summary>
    public void BeginScrub() => IsScrubbing = true;

    /// <summary>Loads (debounced, cancellable) the seek-preview frame for the scrubbed position.</summary>
    public async Task UpdateScrubPreviewAsync(double seconds)
    {
        _previewCts?.Cancel();
        var cts = _previewCts = new CancellationTokenSource();
        try
        {
            await Task.Delay(60, cts.Token).ConfigureAwait(true); // debounce rapid drag
            var path = await RequestSeekPreviewAsync(seconds, cts.Token).ConfigureAwait(true);
            if (!cts.Token.IsCancellationRequested) SeekPreviewPath = path;
        }
        catch (OperationCanceledException) { /* superseded by a newer hover */ }
    }

    /// <summary>Commits the scrub: seeks the engine to ScrubPosition and ends the gesture.</summary>
    public void CommitScrub()
    {
        engine.SeekTo(ScrubPosition);
        PositionSeconds = ScrubPosition;
        IsScrubbing = false;
        SeekPreviewPath = null;
        _previewCts?.Cancel();
        CanResume = false; // a manual seek dismisses the resume offer
    }

    partial void OnSelectedAudioTrackChanged(TrackOption? value)
    {
        if (value is not null) engine.SetAudioTrack(value.Id);
    }

    partial void OnSelectedSubtitleTrackChanged(TrackOption? value)
    {
        if (value is not null) engine.SetSubtitleTrack(value.Id);
    }

    /// <summary>Re-reads live track lists from the engine (call when media is ready / on demand).</summary>
    public void RefreshTracks()
    {
        AudioTracks.Clear();
        foreach (var t in engine.GetAudioTracks()) AudioTracks.Add(t);
        SubtitleTracks.Clear();
        foreach (var t in engine.GetSubtitleTracks()) SubtitleTracks.Add(t);

        OnPropertyChanged(nameof(HasMultipleAudioTracks));
        OnPropertyChanged(nameof(HasSubtitleTracks));
    }

    [RelayCommand]
    private void AddSubtitleFile()
    {
        if (_current is not { } cur) return;
        var folder = Path.GetDirectoryName(cur.FilePath);
        var path = subtitlePicker.PickSubtitle(folder);
        if (string.IsNullOrEmpty(path)) return;
        engine.AddSubtitle(path);
        RefreshTracks();
        SelectedSubtitleTrack = SubtitleTracks.LastOrDefault(t => t.Id != TrackOption.SubtitlesOffId)
                                ?? SelectedSubtitleTrack;
    }

    [RelayCommand]
    private void ToggleFullscreen() => IsFullscreen = !IsFullscreen;

    // ===== B-VM: skip + mute commands =========================================

    [RelayCommand]
    private void SkipBack10()
    {
        engine.SeekTo(Math.Max(0, PositionSeconds - 10));
        ShowSkipFeedback("−10s");
    }

    [RelayCommand]
    private void SkipForward30()
    {
        var target = PositionSeconds + 30;
        if (LengthSeconds > 0) target = Math.Min(LengthSeconds, target);
        engine.SeekTo(target);
        ShowSkipFeedback("+30s");
    }

    [ObservableProperty]
    private bool _isMuted;

    private int _volumeBeforeMute = 100;

    [RelayCommand]
    private void ToggleMute()
    {
        if (IsMuted)
        {
            Volume = _volumeBeforeMute;
            IsMuted = false;
        }
        else
        {
            _volumeBeforeMute = Volume == 0 ? 100 : Volume;
            Volume = 0;
            IsMuted = true;
        }
    }

    // ==========================================================================

    // ===== D-VM: speed / aspect / audio-normalize =============================

    public IReadOnlyList<double> SpeedPresets { get; } = new double[] { 0.5, 0.75, 1.0, 1.25, 1.5, 2.0 };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RateLabel))]
    private double _playbackRate = 1.0;

    partial void OnPlaybackRateChanged(double v) => engine.Rate = v;

    /// <summary>Sets the playback rate from a string (e.g. "1.5") or double. Called from XAML button commands.</summary>
    [RelayCommand]
    private void SetPlaybackRate(object? v)
    {
        PlaybackRate = v switch
        {
            double d => d,
            string s when double.TryParse(s, System.Globalization.NumberStyles.Any,
                                          System.Globalization.CultureInfo.InvariantCulture, out var d) => d,
            _ => PlaybackRate,
        };
    }

    public string RateLabel => PlaybackRate == 1.0 ? "1×" : $"{PlaybackRate:0.##}×";

    // Aspect/zoom presets
    public sealed record AspectPreset(string Label, string? Ratio, float Scale);

    private static readonly AspectPreset[] _aspectPresets =
    {
        new("Default", null, 0f),
        new("16:9",    "16:9", 0f),
        new("4:3",     "4:3",  0f),
        new("Fill",    null,   1f),
    };

    public IReadOnlyList<AspectPreset> AspectPresets { get; } = _aspectPresets;

    [ObservableProperty]
    private AspectPreset _selectedAspect = _aspectPresets[0];

    partial void OnSelectedAspectChanged(AspectPreset p)
    {
        engine.AspectRatio = p.Ratio;
        engine.Scale = p.Scale;
    }

    [RelayCommand]
    private void CycleAspect()
    {
        var idx = Array.IndexOf(_aspectPresets, SelectedAspect);
        SelectedAspect = _aspectPresets[(idx + 1) % _aspectPresets.Length];
    }

    // Audio normalize (only when supported)
    public bool CanNormalizeVolume => engine.SupportsVolumeNormalize;

    [ObservableProperty]
    private bool _volumeNormalizeEnabled;

    partial void OnVolumeNormalizeEnabledChanged(bool v) => engine.VolumeNormalizeEnabled = v;

    // ==========================================================================

    // ===== E1: A-B repeat =====================================================

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAbRepeatActive))]
    private double? _repeatStartSeconds;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAbRepeatActive))]
    private double? _repeatEndSeconds;

    public bool IsAbRepeatActive =>
        RepeatStartSeconds is { } a && RepeatEndSeconds is { } b && b > a;

    [RelayCommand]
    private void SetRepeatA() => RepeatStartSeconds = PositionSeconds;

    [RelayCommand]
    private void SetRepeatB()
    {
        if (RepeatStartSeconds is { } a && PositionSeconds > a)
            RepeatEndSeconds = PositionSeconds;
    }

    /// <summary>True while we're waiting for the engine to settle after a SeekTo(A) call.
    /// Prevents position ticks that arrive during seek buffering from firing SeekTo(A) repeatedly.</summary>
    private bool _abSeeking;

    [RelayCommand]
    private void ClearAbRepeat()
    {
        RepeatStartSeconds = null;
        RepeatEndSeconds = null;
        _abSeeking = false;
    }

    // ==========================================================================

    // ===== E3: skip feedback badge ============================================

    /// <summary>How long (ms) the skip and volume feedback badges stay visible.</summary>
    private const int FeedbackBadgeMs = 700;

    [ObservableProperty]
    private string? _skipFeedback;

    private DispatcherTimer? _skipFeedbackTimer;

    private void ShowSkipFeedback(string text)
    {
        SkipFeedback = text;
        // Only start a DispatcherTimer when running in a real WPF application.
        // In unit-test context (no Application.Current), the property is set
        // synchronously and tests can assert it immediately.
        if (System.Windows.Application.Current is not null)
        {
            if (_skipFeedbackTimer is null)
            {
                _skipFeedbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(FeedbackBadgeMs) };
                _skipFeedbackTimer.Tick += (_, _) =>
                {
                    _skipFeedbackTimer.Stop();
                    SkipFeedback = null;
                };
            }
            _skipFeedbackTimer.Stop();
            _skipFeedbackTimer.Start();
        }
    }

    // ==========================================================================

    // ===== E4: volume-scroll feedback badge ===================================

    [ObservableProperty]
    private string? _volumeFeedback;

    private DispatcherTimer? _volumeFeedbackTimer;

    /// <summary>Called by the view's MouseWheel handler to adjust volume by ±5 and show feedback.</summary>
    public void AdjustVolumeByWheel(int delta)
    {
        var newVol = Math.Clamp(Volume + (delta > 0 ? 5 : -5), 0, 100);
        Volume = newVol;
        VolumeFeedback = $"Volume {newVol}%";

        if (System.Windows.Application.Current is not null)
        {
            if (_volumeFeedbackTimer is null)
            {
                _volumeFeedbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(FeedbackBadgeMs) };
                _volumeFeedbackTimer.Tick += (_, _) =>
                {
                    _volumeFeedbackTimer.Stop();
                    VolumeFeedback = null;
                };
            }
            _volumeFeedbackTimer.Stop();
            _volumeFeedbackTimer.Start();
        }
    }

    // ==========================================================================

    // ===== E5: Play from beginning / IsCompleted ==============================

    /// <summary>True when the video was marked watched (Ended fired or it was already watched when opened).
    /// Used to surface the "Play from beginning" affordance.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCompleted))]
    private bool _currentWatched;

    public bool IsCompleted => CurrentWatched;

    [RelayCommand]
    private void PlayFromBeginning()
    {
        engine.SeekTo(0);
        CanResume = false;
        CurrentWatched = false;
    }

    // ==========================================================================

    /// <summary>Raised after the finished video is marked watched. The host decides what (if anything) plays next.</summary>
    public event EventHandler<EpisodeView>? PlaybackEnded;

    /// <summary>Test hook: simulates the engine reaching the end of the current episode.</summary>
    public void RaisePlaybackEndedForTest(EpisodeView finished) => PlaybackEnded?.Invoke(this, finished);

    /// <summary>Loads an episode, starts playback, and prepares a resume offer if one applies.</summary>
    public void Open(EpisodeView episode)
    {
        // Stop any outgoing media before switching (manual play of a different episode while one is
        // running). No resume flush here: end-of-media auto-next has already marked the previous video
        // watched and cleared its resume, and the periodic tick keeps a mid-play switch's resume current —
        // flushing would re-write a resume position for a just-watched episode.
        if (_current is not null)
            engine.Stop();

        PlaybackError = null;
        if (episode.Missing || !System.IO.File.Exists(episode.FilePath))
        {
            PlaybackError = $"File not found:\n{episode.FilePath}";
            Title = episode.Title;
            return;
        }

        _current = episode;
        OnPropertyChanged(nameof(CurrentFilePath));
        OnPropertyChanged(nameof(CanAddSubtitle));

        // Stop any pending feedback badge timers so a ~700ms timer from the previous episode
        // can't clobber the new episode's badge under autoplay/episode-switch.
        _skipFeedbackTimer?.Stop();
        SkipFeedback = null;
        _volumeFeedbackTimer?.Stop();
        VolumeFeedback = null;

        _lastSavedAt = 0;
        _length = 0;
        ScrubPosition = 0;
        SeekPreviewPath = null;
        Title = episode.Title;
        CanResume = false;
        IsMuted = false;
        ResumePositionSeconds = library.GetResumePosition(episode.VideoId) ?? 0;

        // D: reset ephemeral playback-speed / aspect per-open
        PlaybackRate = 1.0;
        SelectedAspect = AspectPresets[0];
        VolumeNormalizeEnabled = false;

        // E1: reset A-B repeat per-open
        RepeatStartSeconds = null;
        RepeatEndSeconds = null;
        _abSeeking = false;

        // E5: mark completed if the episode was already watched
        CurrentWatched = watch.IsWatched(episode.VideoId);

        engine.PositionChanged -= OnPositionChanged;
        engine.LengthChanged -= OnLengthChanged;
        engine.Ended -= OnEnded;
        engine.EncounteredError -= OnEngineError;
        engine.TracksChanged -= OnTracksChanged;
        engine.PositionChanged += OnPositionChanged;
        engine.LengthChanged += OnLengthChanged;
        engine.Ended += OnEnded;
        engine.EncounteredError += OnEngineError;
        engine.TracksChanged += OnTracksChanged;

        engine.Load(episode.FilePath);
        engine.Play();
        IsPlaying = true;
    }

    private void OnLengthChanged(object? sender, double seconds)
    {
        _length = seconds;
        LengthSeconds = seconds;
        if (_current is { } cur)
        {
            var saved = library.GetResumePosition(cur.VideoId) ?? 0;
            CanResume = resumePolicy.ShouldOfferResume(saved, seconds);
            ResumePositionSeconds = saved;
        }
    }

    private void OnPositionChanged(object? sender, double seconds)
    {
        PositionSeconds = seconds;
        if (!IsScrubbing) ScrubPosition = seconds;
        if (_current is { } cur && resumePolicy.ShouldSaveOnTick(_lastSavedAt, seconds))
        {
            library.SetResumePosition(cur.VideoId, seconds);
            _lastSavedAt = seconds;
        }

        // E1: enforce A-B repeat loop (with re-entrancy guard to avoid spamming SeekTo
        // while the seek settles — libVLC keeps firing position ticks during buffering).
        if (IsAbRepeatActive)
        {
            var endSec = RepeatEndSeconds!.Value; // RepeatEndSeconds non-null when IsAbRepeatActive
            if (!_abSeeking && seconds >= endSec)
            {
                _abSeeking = true;
                engine.SeekTo(RepeatStartSeconds!.Value);
            }
            else if (_abSeeking && seconds < endSec - 1.0)
            {
                _abSeeking = false; // position has settled back below B
            }
        }
    }

    [RelayCommand]
    private void Resume()
    {
        engine.SeekTo(ResumePositionSeconds);
        CanResume = false;
    }

    [RelayCommand]
    private void TogglePlayPause()
    {
        if (engine.IsPlaying)
        {
            engine.Pause();
            IsPlaying = false;
            FlushResume();
        }
        else
        {
            engine.Play();
            IsPlaying = true;
        }
    }

    private void OnEnded(object? sender, EventArgs e)
    {
        IsPlaying = false;
        if (_current is not { } cur) return;
        watch.SetWatched(cur.VideoId, true);
        CurrentWatched = true;  // E5: surface "Play from beginning" affordance
        PlaybackEnded?.Invoke(this, cur);
    }

    private void OnEngineError(object? sender, EventArgs e)
    {
        IsPlaying = false;
        PlaybackError = _current is { } cur
            ? $"This video could not be played:\n{cur.FilePath}"
            : "This video could not be played.";
    }

    /// <summary>
    /// Handles libVLC ESAdded: refreshes audio/subtitle track lists when a new elementary stream
    /// is discovered (e.g. a sidecar subtitle attached via Media.AddSlave — the M13 gap).
    /// ESAdded fires on a libVLC background thread, so we marshal to the UI dispatcher.
    /// In unit tests (no Application.Current), Application.Current?.Dispatcher is null and we
    /// call RefreshTracks directly (the FakePlaybackEngine fires synchronously anyway).
    /// </summary>
    private void OnTracksChanged(object? sender, EventArgs e)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            RefreshTracks();
        else
            dispatcher.BeginInvoke(RefreshTracks);
    }

    /// <summary>Persists the current position immediately (on pause/stop/close).</summary>
    public void FlushResume()
    {
        if (_current is { } cur)
        {
            library.SetResumePosition(cur.VideoId, PositionSeconds);
            _lastSavedAt = PositionSeconds;
        }
    }
}
