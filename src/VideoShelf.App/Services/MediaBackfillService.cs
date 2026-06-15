using System.Threading;
using System.Threading.Tasks;
using VideoShelf.Core.Scanning;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.Services;

/// <summary>Probes every video still missing a duration and persists its duration + resolution.
/// Incremental (only duration IS NULL), crash-safe (each video committed independently),
/// resumable (re-running picks up whatever is still null). Per-file errors are skipped.
/// Concurrent degree is controlled by <see cref="SettingsRepository.GetProbeConcurrency"/>
/// (default 3, clamped 1–8; degree=1 gives safe sequential fallback).</summary>
public sealed class MediaBackfillService(LibraryRepository library, IMediaProbe probe, SettingsRepository? settings = null)
{
    public async Task BackfillAsync(CancellationToken cancellationToken)
    {
        var pending = library.GetVideosNeedingDuration();
        int degree = settings?.GetProbeConcurrency(defaultValue: 3) ?? 3;

        await ProbeScheduler.RunAsync(pending, degree, async (v, ct) =>
        {
            try
            {
                var r = await probe.ProbeAsync(v.FilePath, ct).ConfigureAwait(false);
                // Each write opens its own connection (VideoShelfDb.Open()) — independent commit,
                // crash-safe: a process kill mid-pass loses at most one file's result.
                if (r.DurationSeconds is > 0) library.SetDuration(v.Id, r.DurationSeconds.Value);
                if (r.Width is { } w && r.Height is { } h)
                    library.SetResolution(v.Id, w, h);
            }
            catch (OperationCanceledException) { throw; }
            catch { /* skip this file; a later scan retries it */ }
        }, cancellationToken);
    }
}
