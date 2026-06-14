using System.Threading;
using System.Threading.Tasks;
using VideoShelf.Core.Scanning;

namespace VideoShelf.App.Services;

public interface IScanCoordinator
{
    bool IsBusy { get; }

    /// <summary>
    /// Scans every registered source on a background thread. Idempotent and crash-safe.
    /// Returns the aggregated <see cref="ScanResult"/> across all sources.
    /// </summary>
    Task<ScanResult> ScanAllAsync(CancellationToken cancellationToken);
}
