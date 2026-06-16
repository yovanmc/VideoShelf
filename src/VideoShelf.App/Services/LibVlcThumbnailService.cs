using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LibVLCSharp.Shared;

namespace VideoShelf.App.Services;

/// <summary>
/// Grabs a single representative frame via a headless libVLC MediaPlayer snapshot.
/// Thin by design: real coverage comes from the Phase 6 harness with generated clips.
/// Honors the fail-safe contract — returns false on any error, never throws.
/// </summary>
public sealed class LibVlcThumbnailService : IThumbnailSnapshotter, IDisposable
{
    private readonly LibVLC _libVlc;

    public LibVlcThumbnailService()
    {
        LibVLCSharp.Shared.Core.Initialize(); // loads bundled native libVLC
        _libVlc = new LibVLC("--no-audio", "--no-video-title-show", "--quiet");
    }

    /// <inheritdoc/>
    public Task<bool> TrySnapshotAsync(string videoPath, string outputPngPath, CancellationToken cancellationToken)
        // Default position: 10% of duration, capped at 3 s (computed from player.Length after play starts).
        => TrySnapshotCoreAsync(videoPath, outputPngPath, requestedPositionMs: null, cancellationToken);

    /// <inheritdoc/>
    public Task<bool> TrySnapshotAtAsync(string videoPath, string outputPngPath, TimeSpan position, CancellationToken ct)
        => TrySnapshotCoreAsync(videoPath, outputPngPath, requestedPositionMs: (long)position.TotalMilliseconds, ct);

    /// <summary>
    /// Core headless snapshot logic.
    /// <paramref name="requestedPositionMs"/> = null → use the default fraction heuristic;
    /// a non-null value is clamped to [0, duration] before seeking.
    /// </summary>
    private async Task<bool> TrySnapshotCoreAsync(
        string videoPath,
        string outputPngPath,
        long? requestedPositionMs,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(videoPath))
                return false;

            using var media = new Media(_libVlc, new Uri(videoPath));
            using var player = new MediaPlayer(media) { Mute = true };

            var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnPlaying(object? s, EventArgs e) => ready.TrySetResult(true);
            player.Playing += OnPlaying;

            if (!player.Play())
                return false;

            using (cancellationToken.Register(() => ready.TrySetResult(false)))
            {
                var startedTask = await Task.WhenAny(ready.Task, Task.Delay(5000, cancellationToken))
                    .ConfigureAwait(false);
                if (startedTask != ready.Task || !ready.Task.Result)
                {
                    player.Stop();
                    return false;
                }
            }

            // Compute the seek position: explicit (clamped) or default heuristic.
            long durationMs = player.Length;
            long seekMs = requestedPositionMs.HasValue
                ? ClampMs(requestedPositionMs.Value, durationMs)
                : SnapshotPositionHelper.DefaultSeekMs(durationMs);

            if (seekMs > 0)
                player.Time = seekMs;

            await Task.Delay(300, cancellationToken).ConfigureAwait(false);

            var taken = player.TakeSnapshot(0, outputPngPath, 0, 0);
            player.Stop();

            return taken && File.Exists(outputPngPath) && new FileInfo(outputPngPath).Length > 0;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch
        {
            return false; // fail-safe
        }
    }

    /// <summary>Clamps a requested seek position (ms) to [0, durationMs]. Returns 0 when duration unknown.</summary>
    private static long ClampMs(long requestedMs, long durationMs)
    {
        if (durationMs <= 0) return 0;
        if (requestedMs < 0) return 0;
        if (requestedMs > durationMs) return durationMs;
        return requestedMs;
    }

    public void Dispose() => _libVlc.Dispose();
}
