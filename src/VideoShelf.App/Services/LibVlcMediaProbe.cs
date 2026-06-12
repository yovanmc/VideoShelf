using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LibVLCSharp.Shared;
using VideoShelf.Core.Models;

namespace VideoShelf.App.Services;

/// <summary>
/// Briefly opens a file in a headless libVLC MediaPlayer to read its duration and chapter markers.
/// Thin by design: real coverage comes from the integration harness. Fail-safe — never throws for a bad file.
/// </summary>
public sealed class LibVlcMediaProbe : IMediaProbe, IDisposable
{
    private static readonly MediaProbeResult Empty =
        new MediaProbeResult(null, Array.Empty<ChapterRecord>());

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

            // Read chapters — FullChapterDescriptions(int titleIndex); pass -1 = current title.
            var rawChapters = player.FullChapterDescriptions(-1);
            List<ChapterRecord> chapters;
            if (rawChapters is { Length: > 0 })
            {
                chapters = new List<ChapterRecord>(rawChapters.Length);
                for (int i = 0; i < rawChapters.Length; i++)
                {
                    var c = rawChapters[i];
                    // TimeOffset is milliseconds (long); convert to seconds.
                    chapters.Add(new ChapterRecord(i, c.Name ?? string.Empty, c.TimeOffset / 1000.0));
                }
            }
            else
            {
                chapters = new List<ChapterRecord>(0);
            }

            player.Stop();
            return new MediaProbeResult(durationSeconds, chapters);
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
