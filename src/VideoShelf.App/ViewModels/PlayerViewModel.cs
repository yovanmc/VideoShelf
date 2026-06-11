using System;
using System.Collections.ObjectModel;
using System.IO;
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
    ResumePolicy resumePolicy) : ObservableObject
{
    private EpisodeView? _current;
    private double _lastSavedAt;
    private double _length;

    public IPlaybackEngine Engine => engine;

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private string? _playbackError;

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
    public ObservableCollection<ChapterOption> Chapters { get; } = [];

    public bool HasMultipleAudioTracks => AudioTracks.Count > 1;
    public bool HasSubtitleTracks => SubtitleTracks.Count > 1;
    public bool HasChapters => Chapters.Count > 0;

    [ObservableProperty]
    private TrackOption? _selectedAudioTrack;

    [ObservableProperty]
    private TrackOption? _selectedSubtitleTrack;

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

    /// <summary>Produces a seek-preview frame PNG for the given position, or null on failure (fail-safe).</summary>
    public async Task<string?> RequestSeekPreviewAsync(double seconds, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(SeekPreviewDirectory);
            var bucket = (long)seconds; // 1s buckets keep scrubbing cache-friendly
            var target = Path.Combine(SeekPreviewDirectory, $"preview_{bucket}.png");
            if (File.Exists(target) && new FileInfo(target).Length > 0)
                return target;

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

    partial void OnSelectedAudioTrackChanged(TrackOption? value)
    {
        if (value is not null) engine.SetAudioTrack(value.Id);
    }

    partial void OnSelectedSubtitleTrackChanged(TrackOption? value)
    {
        if (value is not null) engine.SetSubtitleTrack(value.Id);
    }

    /// <summary>Re-reads live track/chapter lists from the engine (call when media is ready / on demand).</summary>
    public void RefreshTracks()
    {
        AudioTracks.Clear();
        foreach (var t in engine.GetAudioTracks()) AudioTracks.Add(t);
        SubtitleTracks.Clear();
        foreach (var t in engine.GetSubtitleTracks()) SubtitleTracks.Add(t);
        Chapters.Clear();
        foreach (var c in engine.GetChapters()) Chapters.Add(c);

        OnPropertyChanged(nameof(HasMultipleAudioTracks));
        OnPropertyChanged(nameof(HasSubtitleTracks));
        OnPropertyChanged(nameof(HasChapters));
    }

    [RelayCommand]
    private void NextChapter() => engine.NextChapter();

    [RelayCommand]
    private void PreviousChapter() => engine.PreviousChapter();

    [RelayCommand]
    private void ToggleFullscreen() => IsFullscreen = !IsFullscreen;

    /// <summary>Raised when end-of-media should advance to the next in-series episode (auto-advance only).</summary>
    public event EventHandler<EpisodeView>? NextEpisodeRequested;

    /// <summary>Test hook: simulates the engine reaching the end and requesting the given next episode.</summary>
    public void RaiseNextEpisodeForTest(EpisodeView next) => NextEpisodeRequested?.Invoke(this, next);

    /// <summary>Loads an episode, starts playback, and prepares a resume offer if one applies.</summary>
    public void Open(EpisodeView episode)
    {
        PlaybackError = null;
        if (episode.Missing || !System.IO.File.Exists(episode.FilePath))
        {
            PlaybackError = $"File not found:\n{episode.FilePath}";
            Title = episode.Title;
            return;
        }

        _current = episode;
        _lastSavedAt = 0;
        _length = 0;
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
        if (_current is not { } cur)
            return;

        // Finishing a video marks it watched, which also clears its resume position.
        watch.SetWatched(cur.VideoId, true);

        if (settings.GetAutoAdvanceEpisodes())
        {
            var next = library.GetNextEpisode(cur.SeriesId, cur.EpisodeNo);
            if (next is not null)
                NextEpisodeRequested?.Invoke(this, next);
        }
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
