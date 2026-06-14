using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
    private double _positionSeconds;

    [ObservableProperty]
    private double _lengthSeconds;

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
        _lastSavedAt = 0;
        _length = 0;
        ScrubPosition = 0;
        SeekPreviewPath = null;
        Title = episode.Title;
        CanResume = false;
        ResumePositionSeconds = library.GetResumePosition(episode.VideoId) ?? 0;

        engine.PositionChanged -= OnPositionChanged;
        engine.LengthChanged -= OnLengthChanged;
        engine.Ended -= OnEnded;
        engine.EncounteredError -= OnEngineError;
        engine.PositionChanged += OnPositionChanged;
        engine.LengthChanged += OnLengthChanged;
        engine.Ended += OnEnded;
        engine.EncounteredError += OnEngineError;

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
        PlaybackEnded?.Invoke(this, cur);
    }

    private void OnEngineError(object? sender, EventArgs e)
    {
        IsPlaying = false;
        PlaybackError = _current is { } cur
            ? $"This video could not be played:\n{cur.FilePath}"
            : "This video could not be played.";
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
