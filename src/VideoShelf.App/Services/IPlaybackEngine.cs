using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace VideoShelf.App.Services;

/// <summary>An audio or subtitle track choice. Id is the libVLC track id; -1 means "subtitles off".</summary>
public sealed record TrackOption(int Id, string Label)
{
    public const int SubtitlesOffId = -1;
}

/// <summary>An embedded chapter. Index is the libVLC chapter index (0-based); Name may be empty.
/// StartSeconds is the chapter's start offset in seconds (for scrubber tick marks; 0 if unknown).</summary>
public sealed record ChapterOption(int Index, string Name, double StartSeconds = 0);

/// <summary>
/// Abstracts the libVLC MediaPlayer so all playback decision logic stays unit-testable.
/// Implementations MUST be fail-safe — surface failures via events/return values, never throw into callers.
/// All position/length values are in seconds.
/// </summary>
public interface IPlaybackEngine : IDisposable
{
    // ----- transport -----
    void Load(string filePath);
    void Play();
    void Pause();
    void Stop();
    bool IsPlaying { get; }

    /// <summary>Current playback position in seconds.</summary>
    double Position { get; }
    /// <summary>Media length in seconds (0 until known).</summary>
    double Length { get; }
    void SeekTo(double seconds);

    /// <summary>0..100.</summary>
    int Volume { get; set; }

    // ----- tracks -----
    IReadOnlyList<TrackOption> GetAudioTracks();
    int GetCurrentAudioTrack();
    void SetAudioTrack(int id);

    /// <summary>Subtitle tracks INCLUDING the "subtitles off" option (id == TrackOption.SubtitlesOffId).</summary>
    IReadOnlyList<TrackOption> GetSubtitleTracks();
    int GetCurrentSubtitleTrack();
    void SetSubtitleTrack(int id);
    /// <summary>Attaches an external subtitle file to the currently-loaded media and selects it.</summary>
    void AddSubtitle(string subtitlePath);

    // ----- chapters -----
    IReadOnlyList<ChapterOption> GetChapters();
    void NextChapter();
    void PreviousChapter();

    // ----- frame capture -----
    /// <summary>Saves the current frame to a PNG. Returns false on any failure (fail-safe).</summary>
    bool TrySnapshot(string outputPngPath);
    /// <summary>Renders a preview frame for the given position to a PNG (for seek-preview). Returns false on failure.</summary>
    Task<bool> TryGeneratePreviewFrameAsync(double seconds, string outputPngPath, CancellationToken cancellationToken);

    // ----- events -----
    /// <summary>Fires (roughly per second) with the current position in seconds.</summary>
    event EventHandler<double>? PositionChanged;
    /// <summary>Fires once when the media length becomes known, with the length in seconds.</summary>
    event EventHandler<double>? LengthChanged;
    /// <summary>Fires when playback reaches the natural end of the media.</summary>
    event EventHandler? Ended;
    /// <summary>Fires when the engine hits an unrecoverable error for the loaded media.</summary>
    event EventHandler? EncounteredError;
}
