using System;
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
    private double _positionSeconds;

    [ObservableProperty]
    private double _lengthSeconds;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private bool _canResume;

    [ObservableProperty]
    private double _resumePositionSeconds;

    /// <summary>Loads an episode, starts playback, and prepares a resume offer if one applies.</summary>
    public void Open(EpisodeView episode)
    {
        _current = episode;
        _lastSavedAt = 0;
        _length = 0;
        Title = episode.Title;
        CanResume = false;
        ResumePositionSeconds = library.GetResumePosition(episode.VideoId) ?? 0;

        engine.PositionChanged -= OnPositionChanged;
        engine.LengthChanged -= OnLengthChanged;
        engine.PositionChanged += OnPositionChanged;
        engine.LengthChanged += OnLengthChanged;

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
