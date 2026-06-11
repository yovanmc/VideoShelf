using System.Threading;
using System.Threading.Tasks;

namespace VideoShelf.App.Services;

/// <summary>Returns a disk path to a cached poster thumbnail for a video, or null if unavailable.</summary>
public interface IThumbnailService
{
    Task<string?> GetThumbnailPathAsync(string videoPath, CancellationToken cancellationToken);
}

/// <summary>Low-level frame grab. Implementations write a PNG to outputPngPath and return true on success.
/// Must NOT throw — return false on any failure so the cache can fall back to a placeholder.</summary>
public interface IThumbnailSnapshotter
{
    Task<bool> TrySnapshotAsync(string videoPath, string outputPngPath, CancellationToken cancellationToken);
}
