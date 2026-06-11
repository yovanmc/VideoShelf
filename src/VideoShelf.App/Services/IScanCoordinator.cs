using System.Threading;
using System.Threading.Tasks;

namespace VideoShelf.App.Services;

public interface IScanCoordinator
{
    bool IsBusy { get; }

    /// <summary>Scans every registered source on a background thread. Idempotent and crash-safe.</summary>
    Task ScanAllAsync(CancellationToken cancellationToken);
}
