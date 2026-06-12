using System.Threading;
using System.Threading.Tasks;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.Services;

/// <summary>Probes every video still missing a duration and persists its duration + chapters.
/// Incremental (only duration IS NULL), crash-safe (each video committed independently),
/// resumable (re-running picks up whatever is still null). Per-file errors are skipped.</summary>
public sealed class MediaBackfillService(LibraryRepository library, IMediaProbe probe)
{
    public async Task BackfillAsync(CancellationToken cancellationToken)
    {
        var pending = library.GetVideosNeedingDuration();
        foreach (var v in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var r = await probe.ProbeAsync(v.FilePath, cancellationToken).ConfigureAwait(false);
                if (r.DurationSeconds is > 0) library.SetDuration(v.Id, r.DurationSeconds.Value);
                library.ReplaceChapters(v.Id, r.Chapters);
            }
            catch (OperationCanceledException) { throw; }
            catch { /* skip this file; a later scan retries it */ }
        }
    }
}
