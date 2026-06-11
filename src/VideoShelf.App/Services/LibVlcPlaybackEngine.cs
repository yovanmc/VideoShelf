using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using LibVLCSharp.Shared;

namespace VideoShelf.App.Services;

/// <summary>
/// Thin libVLC-backed IPlaybackEngine. Owns a LibVLC + MediaPlayer; the View binds the MediaPlayer
/// to a LibVLCSharp.WPF VideoView. Fail-safe by contract: errors raise EncounteredError, never throw.
/// Not unit-tested (integration); covered by the Phase 6 harness with generated clips.
/// </summary>
public sealed class LibVlcPlaybackEngine : IPlaybackEngine
{
    private readonly LibVLC _libVlc;
    private readonly MediaPlayer _player;
    private readonly Dispatcher _dispatcher;

    /// <summary>The underlying libVLC player, for the VideoView to host. App-internal use only.</summary>
    public MediaPlayer MediaPlayer => _player;

    public LibVlcPlaybackEngine()
    {
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

        LibVLCSharp.Shared.Core.Initialize();
        _libVlc = new LibVLC("--no-video-title-show", "--quiet");
        _player = new MediaPlayer(_libVlc);

        // libVLC raises these on its own background threads. Marshal to the UI thread with BeginInvoke
        // (non-blocking) so consumers can touch UI-bound state AND re-enter the player (Stop/Load/Play on
        // auto-next) without doing so from inside a libVLC callback thread — a known deadlock hazard.
        _player.TimeChanged += (_, e) => Raise(() => PositionChanged?.Invoke(this, e.Time / 1000.0));
        _player.LengthChanged += (_, e) => Raise(() => LengthChanged?.Invoke(this, e.Length / 1000.0));
        _player.EndReached += (_, _) => Raise(() => Ended?.Invoke(this, EventArgs.Empty));
        _player.EncounteredError += (_, _) => Raise(() => EncounteredError?.Invoke(this, EventArgs.Empty));
    }

    /// <summary>Runs an action on the UI dispatcher thread (async, non-blocking).</summary>
    private void Raise(Action action)
    {
        if (_dispatcher.CheckAccess())
            action();
        else
            _dispatcher.BeginInvoke(action);
    }

    public void Load(string filePath)
    {
        try
        {
            var media = new Media(_libVlc, new Uri(filePath));
            _player.Media = media;
            media.Dispose(); // MediaPlayer retains its own reference
        }
        catch
        {
            EncounteredError?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Play() { try { _player.Play(); } catch { EncounteredError?.Invoke(this, EventArgs.Empty); } }
    public void Pause() { try { _player.SetPause(true); } catch { } }
    public void Stop() { try { _player.Stop(); } catch { } }
    public bool IsPlaying => _player.IsPlaying;

    public double Position => _player.Time / 1000.0;
    public double Length => _player.Length / 1000.0;
    public void SeekTo(double seconds) { try { _player.Time = (long)(seconds * 1000); } catch { } }

    public int Volume
    {
        get => _player.Volume;
        set { try { _player.Volume = Math.Clamp(value, 0, 100); } catch { } }
    }

    public IReadOnlyList<TrackOption> GetAudioTracks()
    {
        var list = new List<TrackOption>();
        try
        {
            foreach (var d in _player.AudioTrackDescription)
                if (d.Id >= 0) // -1 is the libVLC "disable audio" pseudo-track; we don't surface it
                    list.Add(new TrackOption(d.Id, d.Name ?? $"Audio {d.Id}"));
        }
        catch { }
        return list;
    }

    public int GetCurrentAudioTrack() { try { return _player.AudioTrack; } catch { return -1; } }
    public void SetAudioTrack(int id) { try { _player.SetAudioTrack(id); } catch { } }

    public IReadOnlyList<TrackOption> GetSubtitleTracks()
    {
        var list = new List<TrackOption>();
        try
        {
            // Always offer "subtitles off" first.
            list.Add(new TrackOption(TrackOption.SubtitlesOffId, "Off"));
            foreach (var d in _player.SpuDescription)
                if (d.Id >= 0)
                    list.Add(new TrackOption(d.Id, d.Name ?? $"Subtitle {d.Id}"));
        }
        catch { }
        return list;
    }

    public int GetCurrentSubtitleTrack() { try { return _player.Spu; } catch { return TrackOption.SubtitlesOffId; } }
    public void SetSubtitleTrack(int id) { try { _player.SetSpu(id); } catch { } }

    public IReadOnlyList<ChapterOption> GetChapters()
    {
        var list = new List<ChapterOption>();
        try
        {
            var chapters = _player.FullChapterDescriptions();
            if (chapters is not null)
                for (var i = 0; i < chapters.Length; i++)
                    list.Add(new ChapterOption(i, chapters[i].Name ?? $"Chapter {i + 1}"));
        }
        catch { }
        return list;
    }

    public void NextChapter() { try { _player.NextChapter(); } catch { } }
    public void PreviousChapter() { try { _player.PreviousChapter(); } catch { } }

    public bool TrySnapshot(string outputPngPath)
    {
        try
        {
            return _player.TakeSnapshot(0, outputPngPath, 0, 0)
                && File.Exists(outputPngPath) && new FileInfo(outputPngPath).Length > 0;
        }
        catch { return false; }
    }

    public async Task<bool> TryGeneratePreviewFrameAsync(double seconds, string outputPngPath, CancellationToken cancellationToken)
    {
        // Seek-preview uses the live player's snapshot at the hovered time. A dedicated off-screen
        // decode is a Phase 6 refinement; here we snapshot the current frame fail-safely.
        try
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            return TrySnapshot(outputPngPath);
        }
        catch { return false; }
    }

    public event EventHandler<double>? PositionChanged;
    public event EventHandler<double>? LengthChanged;
    public event EventHandler? Ended;
    public event EventHandler? EncounteredError;

    public void Dispose()
    {
        try { _player.Dispose(); } catch { }
        try { _libVlc.Dispose(); } catch { }
    }
}
