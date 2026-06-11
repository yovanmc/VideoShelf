using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace VideoShelf.App.Services;

/// <summary>
/// No-op IPlaybackEngine used in DI until the concrete LibVlcPlaybackEngine is wired (Task 16).
/// Replaced by LibVlcPlaybackEngine registration in ServiceCollectionExtensions.
/// </summary>
internal sealed class NullPlaybackEngine : IPlaybackEngine
{
    public bool IsPlaying => false;
    public double Position => 0;
    public double Length => 0;
    public int Volume { get; set; }

    public void Load(string filePath) { }
    public void Play() { }
    public void Pause() { }
    public void Stop() { }
    public void SeekTo(double seconds) { }

    public IReadOnlyList<TrackOption> GetAudioTracks() => Array.Empty<TrackOption>();
    public int GetCurrentAudioTrack() => -1;
    public void SetAudioTrack(int id) { }

    public IReadOnlyList<TrackOption> GetSubtitleTracks() => Array.Empty<TrackOption>();
    public int GetCurrentSubtitleTrack() => TrackOption.SubtitlesOffId;
    public void SetSubtitleTrack(int id) { }

    public IReadOnlyList<ChapterOption> GetChapters() => Array.Empty<ChapterOption>();
    public void NextChapter() { }
    public void PreviousChapter() { }

    public bool TrySnapshot(string outputPngPath) => false;
    public Task<bool> TryGeneratePreviewFrameAsync(double seconds, string outputPngPath, CancellationToken cancellationToken)
        => Task.FromResult(false);

#pragma warning disable CS0067 // unused events are required by the interface
    public event EventHandler<double>? PositionChanged;
    public event EventHandler<double>? LengthChanged;
    public event EventHandler? Ended;
    public event EventHandler? EncounteredError;
#pragma warning restore CS0067

    public void Dispose() { }
}
