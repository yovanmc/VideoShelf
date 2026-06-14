using System.Threading;
using System.Threading.Tasks;
using VideoShelf.Core.Scanning;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.Services;

public sealed class ScanCoordinator(LibraryRepository library, ScanService scanService) : IScanCoordinator
{
    private volatile bool _busy;

    public bool IsBusy => _busy;

    public async Task<ScanResult> ScanAllAsync(CancellationToken cancellationToken)
    {
        _busy = true;
        try
        {
            return await Task.Run(() =>
            {
                int totalAdded = 0, totalUpdated = 0, totalRestored = 0, totalMissing = 0;
                foreach (var source in library.GetSources())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var result = scanService.ScanSource(source.RootPath, source.DisplayName);
                    totalAdded   += result.Added;
                    totalUpdated += result.Updated;
                    totalRestored += result.Restored;
                    totalMissing += result.Missing;
                }
                return new ScanResult(totalAdded, totalUpdated, totalRestored, totalMissing);
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _busy = false;
        }
    }
}
