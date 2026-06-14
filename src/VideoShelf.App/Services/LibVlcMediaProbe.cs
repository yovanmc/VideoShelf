using System;
using System.Threading;
using System.Threading.Tasks;
using LibVLCSharp.Shared;

namespace VideoShelf.App.Services;

/// <summary>
/// Briefly opens a file in a headless libVLC MediaPlayer to read its duration and video resolution.
/// Thin by design: real coverage comes from the integration harness. Fail-safe — never throws for a bad file.
/// </summary>
public sealed class LibVlcMediaProbe : IMediaProbe, IDisposable
{
    private static readonly MediaProbeResult Empty = new MediaProbeResult(null, null, null);

    private readonly LibVLC _libVlc;

    public LibVlcMediaProbe()
    {
        LibVLCSharp.Shared.Core.Initialize();
        _libVlc = new LibVLC("--no-audio", "--no-video-title-show", "--quiet");
    }

    public async Task<MediaProbeResult> ProbeAsync(string path, CancellationToken cancellationToken)
    {
        MediaPlayer? player = null;
        Media? media = null;
        try
        {
            media = new Media(_libVlc, new Uri(path));
            player = new MediaPlayer(media) { Mute = true };

            // Mirror LibVlcThumbnailService: use a TCS signalled from the Playing handler.
            // Do NOT call back into libVLC from the handler itself.
            var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnPlaying(object? s, EventArgs e) => ready.TrySetResult(true);
            player.Playing += OnPlaying;

            if (!player.Play())
                return Empty;

            // Wait up to 10 s for Playing, honour cancellation.
            using (cancellationToken.Register(() => ready.TrySetResult(false)))
            {
                var startedTask = await Task.WhenAny(ready.Task, Task.Delay(10_000, cancellationToken))
                    .ConfigureAwait(false);
                if (startedTask != ready.Task || !ready.Task.Result)
                {
                    player.Stop();
                    return Empty;
                }
            }

            // Read duration.
            var lenMs = player.Length;
            double? durationSeconds = lenMs > 0 ? lenMs / 1000.0 : null;

            // Read video pixel size via Size(0, ref px, ref py).
            // Verified via reflection on LibVLCSharp 3.9.7.1:
            //   bool Size(uint num, ref uint px, ref uint py)
            // Fallback: media.Tracks, TrackType.Video → .Data.Video.Width/.Height (fields).
            int? width = null;
            int? height = null;
            try
            {
                uint px = 0, py = 0;
                if (player.Size(0, ref px, ref py) && px > 0 && py > 0)
                {
                    width = (int)px;
                    height = (int)py;
                }
                else
                {
                    // Fallback: parse Tracks from the Media object
                    var tracks = player.Media?.Tracks;
                    if (tracks != null)
                    {
                        foreach (var t in tracks)
                        {
                            if (t.TrackType == TrackType.Video && t.Data.Video.Width > 0 && t.Data.Video.Height > 0)
                            {
                                width = (int)t.Data.Video.Width;
                                height = (int)t.Data.Video.Height;
                                break;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Fail-safe: resolution is optional; never block the probe
            }

            player.Stop();
            return new MediaProbeResult(durationSeconds, width, height);
        }
        catch (OperationCanceledException)
        {
            throw; // cancellation may propagate
        }
        catch
        {
            return Empty; // bad file, parse failure, etc. — fail-safe
        }
        finally
        {
            player?.Dispose();
            media?.Dispose();
        }
    }

    public void Dispose() => _libVlc.Dispose();
}
