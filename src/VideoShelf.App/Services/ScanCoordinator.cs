using System.Threading;
using System.Threading.Tasks;
using VideoShelf.Core.Scanning;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.Services;

public sealed class ScanCoordinator(LibraryRepository library, ScanService scanService) : IScanCoordinator
{
    private volatile bool _busy;

    public bool IsBusy => _busy;

    public async Task ScanAllAsync(CancellationToken cancellationToken)
    {
        _busy = true;
        try
        {
            await Task.Run(() =>
            {
                foreach (var source in library.GetSources())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    scanService.ScanSource(source.RootPath, source.DisplayName);
                }
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _busy = false;
        }
    }
}
