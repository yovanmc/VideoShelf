using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VideoShelf.App.Services;

namespace VideoShelf.App.Tests.TestSupport;

/// <summary>An in-memory IPlaybackEngine for deterministic view-model tests.
/// Tests drive it via the Raise* helpers and read the recorded state.</summary>
public sealed class FakePlaybackEngine : IPlaybackEngine
{
    public string? LoadedPath { get; private set; }
    public bool IsPlaying { get; private set; }
    public bool Disposed { get; private set; }
    public int SnapshotCount { get; private set; }
    public bool SnapshotShouldFail { get; set; }
    public List<double> Seeks { get; } = new();
    public double Position { get; private set; }
    public double Length { get; set; }
    public int Volume { get; set; } = 100;

    public List<TrackOption> AudioTracks { get; } = new();
    public List<TrackOption> SubtitleTracks { get; } = new();

    private int _currentAudio = -1;
    private int _currentSubtitle = TrackOption.SubtitlesOffId;

    public void Load(string filePath) { LoadedPath = filePath; }
    public void Play() => IsPlaying = true;
    public void Pause() => IsPlaying = false;
    public void Stop() => IsPlaying = false;
    public void SeekTo(double seconds) { Position = seconds; Seeks.Add(seconds); }

    public IReadOnlyList<TrackOption> GetAudioTracks() => AudioTracks;
    public int GetCurrentAudioTrack() => _currentAudio;
    public void SetAudioTrack(int id) => _currentAudio = id;

    public IReadOnlyList<TrackOption> GetSubtitleTracks() => SubtitleTracks;
    public int GetCurrentSubtitleTrack() => _currentSubtitle;
    public void SetSubtitleTrack(int id) => _currentSubtitle = id;

    public List<string> AddedSubtitles { get; } = new();
    public void AddSubtitle(string subtitlePath)
    {
        AddedSubtitles.Add(subtitlePath);
        // simulate the new track surfacing in the picker:
        SubtitleTracks.Add(new TrackOption(SubtitleTracks.Count, System.IO.Path.GetFileName(subtitlePath)));
    }

    public bool TrySnapshot(string outputPngPath)
    {
        SnapshotCount++;
        return !SnapshotShouldFail;
    }

    public double? LastPreviewSeconds { get; private set; }

    public Task<bool> TryGeneratePreviewFrameAsync(double seconds, string outputPngPath, CancellationToken cancellationToken)
    {
        LastPreviewSeconds = seconds;
        if (SnapshotShouldFail) return Task.FromResult(false);
        try { System.IO.File.WriteAllBytes(outputPngPath, new byte[] { 0x89, 0x50, 0x4E, 0x47 }); } catch { }
        return Task.FromResult(true);
    }

    public event EventHandler<double>? PositionChanged;
    public event EventHandler<double>? LengthChanged;
    public event EventHandler? Ended;
    public event EventHandler? EncounteredError;

    // ----- test drivers -----
    public void RaisePosition(double seconds) { Position = seconds; PositionChanged?.Invoke(this, seconds); }
    public void RaiseLength(double seconds) { Length = seconds; LengthChanged?.Invoke(this, seconds); }
    public void RaiseEnded() { IsPlaying = false; Ended?.Invoke(this, EventArgs.Empty); }
    public void RaiseError() => EncounteredError?.Invoke(this, EventArgs.Empty);

    public void Dispose() => Disposed = true;
}
