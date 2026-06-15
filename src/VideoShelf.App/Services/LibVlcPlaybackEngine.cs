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
    private MediaPlayer? _previewPlayer;
    private string? _previewMediaPath;
    private bool _volumeNormalizeEnabled;

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
        // ESAdded fires on a libVLC thread. We forward raw (no dispatch) so each subscriber
        // marshals to its own thread — the VM does BeginInvoke in its handler (C4).
        _player.ESAdded += OnESAdded;
    }

    private void OnESAdded(object? sender, LibVLCSharp.Shared.MediaPlayerESAddedEventArgs e)
        => TracksChanged?.Invoke(this, EventArgs.Empty);

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
            try
            {
                var dir = System.IO.Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && System.IO.Directory.Exists(dir))
                {
                    foreach (var sub in VideoShelf.Core.Playback.SubtitleSidecars.Find(filePath, System.IO.Directory.GetFiles(dir)))
                        media.AddSlave(MediaSlaveType.Subtitle, 4, new Uri(sub).AbsoluteUri);
                }
            }
            catch { /* sidecar attach is best-effort; never block playback */ }
            media.Dispose(); // MediaPlayer retains its own reference
        }
        catch
        {
            EncounteredError?.Invoke(this, EventArgs.Empty);
        }
    }

    public void AddSubtitle(string subtitlePath)
    {
        try { _player.AddSlave(MediaSlaveType.Subtitle, new Uri(subtitlePath).AbsoluteUri, true); }
        catch { }
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

    // ----- C1: playback speed -----
    // _player.Rate is get-only (libVLC 3.x); set via _player.SetRate(float).
    // The interface exposes double; we cast to/from float.
    // Clamp is authoritative here; callers outside this assembly are not expected to clamp.
    internal const double MinRate = 0.5;
    internal const double MaxRate = 2.0;

    /// <summary>Clamps a rate value to the supported [MinRate, MaxRate] range. Pure helper; unit-testable.</summary>
    internal static double ClampRate(double value) => Math.Clamp(value, MinRate, MaxRate);

    public double Rate
    {
        get { try { return _player.Rate; } catch { return 1.0; } }
        set
        {
            var clamped = (float)ClampRate(value);
            try { _player.SetRate(clamped); } catch { }
        }
    }

    // ----- C1: aspect ratio / zoom -----
    public string? AspectRatio
    {
        get { try { return _player.AspectRatio; } catch { return null; } }
        set { try { _player.AspectRatio = value ?? string.Empty; } catch { } }
    }

    public float Scale
    {
        get { try { return _player.Scale; } catch { return 0f; } }
        set { try { _player.Scale = value; } catch { } }
    }

    // ----- C1: volume normalization -----
    // normvol is a libVLC audio filter applied at media-load time (not a runtime property).
    // SupportsVolumeNormalize = true means the Media.AddOption approach is wired and applied
    // on the next Load. AUDIBLE EFFECT ON WINDOWS OUTPUT IS UNVERIFIED — requires owner listening test.
    // If the owner reports no audible effect, flip this to => false to hide the Group D toggle.
    public bool SupportsVolumeNormalize => true;

    public bool VolumeNormalizeEnabled
    {
        get => _volumeNormalizeEnabled;
        set => _volumeNormalizeEnabled = value;
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
        try
        {
            if (await TryOffScreenPreviewAsync(seconds, outputPngPath, cancellationToken).ConfigureAwait(false))
                return true;
        }
        catch { /* fall through to live snapshot */ }

        // Fail-safe fallback: snapshot the live frame (previous behaviour). Always non-throwing.
        try { return TrySnapshot(outputPngPath); }
        catch { return false; }
    }

    /// <summary>Decodes the frame at <paramref name="seconds"/> on a hidden, audio-disabled MediaPlayer
    /// (so live playback is untouched) and snapshots it. Returns false (caller falls back) if a frame
    /// can't be produced within a short budget.</summary>
    private async Task<bool> TryOffScreenPreviewAsync(double seconds, string outputPngPath, CancellationToken ct)
    {
        var srcPath = _player.Media?.Mrl;
        if (string.IsNullOrEmpty(srcPath)) return false;

        if (_previewPlayer is null)
        {
            _previewPlayer = new MediaPlayer(_libVlc) { Mute = true };
            _previewMediaPath = null;
        }
        if (_previewMediaPath != srcPath)
        {
            using var m = new Media(_libVlc, new Uri(srcPath), ":no-audio");
            _previewPlayer.Media = m;
            _previewMediaPath = srcPath;
        }

        if (!_previewPlayer.IsPlaying) _previewPlayer.Play();
        _previewPlayer.Time = (long)(seconds * 1000);

        var deadline = DateTime.UtcNow.AddMilliseconds(700);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (_previewPlayer.TakeSnapshot(0, outputPngPath, 0, 0)
                && File.Exists(outputPngPath) && new FileInfo(outputPngPath).Length > 0)
            {
                _previewPlayer.SetPause(true);
                return true;
            }
            await Task.Delay(50, ct).ConfigureAwait(false);
        }
        return false;
    }

    public event EventHandler<double>? PositionChanged;
    public event EventHandler<double>? LengthChanged;
    public event EventHandler? Ended;
    public event EventHandler? EncounteredError;
    public event EventHandler? TracksChanged;

    public void Dispose()
    {
        try { _player.ESAdded -= OnESAdded; } catch { }
        try { _previewPlayer?.Dispose(); } catch { }
        try { _player.Dispose(); } catch { }
        try { _libVlc.Dispose(); } catch { }
    }
}
