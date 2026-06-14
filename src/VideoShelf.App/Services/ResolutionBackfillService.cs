using System.Threading;
using System.Threading.Tasks;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.Services;

/// <summary>Probes every present video still missing width/height and persists the resolution.
/// Incremental (only width IS NULL), crash-safe (each video committed independently),
/// resumable (re-running picks up whatever is still null). Per-file errors are skipped.</summary>
public sealed class ResolutionBackfillService(LibraryRepository library, IMediaProbe probe)
{
    public async Task BackfillAsync(CancellationToken cancellationToken)
    {
        var pending = library.GetVideosNeedingResolution();
        foreach (var v in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var r = await probe.ProbeAsync(v.FilePath, cancellationToken).ConfigureAwait(false);
                if (r.Width is { } w && r.Height is { } h)
                    library.SetResolution(v.Id, w, h);
            }
            catch (OperationCanceledException) { throw; }
            catch { /* skip this file; a later scan retries it */ }
        }
    }
}
