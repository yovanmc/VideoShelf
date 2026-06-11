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

    public async Task<bool> TrySnapshotAsync(string videoPath, string outputPngPath, CancellationToken cancellationToken)
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

            // Seek a little in so we don't capture a black leader frame, then snapshot.
            if (player.Length > 0)
                player.Time = Math.Min(player.Length / 10, 3000);
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

    public void Dispose() => _libVlc.Dispose();
}
