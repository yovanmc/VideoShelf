using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VideoShelf.App.Services;

/// <summary>
/// Caches poster thumbnails as PNGs under a directory, keyed by a hash of the video's full path.
/// Fail-safe: any snapshot failure yields null (a placeholder), never an exception into the UI.
/// </summary>
public sealed class ThumbnailCache(string cacheDirectory, IThumbnailSnapshotter snapshotter) : IThumbnailService
{
    public static string CacheFileName(string videoPath)
    {
        var bytes = Encoding.UTF8.GetBytes(videoPath);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash) + ".png";
    }

    public async Task<string?> GetThumbnailPathAsync(string videoPath, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(cacheDirectory);
            var target = Path.Combine(cacheDirectory, CacheFileName(videoPath));

            if (File.Exists(target) && new FileInfo(target).Length > 0)
                return target;

            // Snapshot to a temp file, then move into place — a crash mid-write never leaves a
            // corrupt cache entry (defensive: place-then-rename).
            var temp = target + ".tmp";
            var ok = await snapshotter.TrySnapshotAsync(videoPath, temp, cancellationToken)
                .ConfigureAwait(false);

            if (!ok || !File.Exists(temp) || new FileInfo(temp).Length == 0)
            {
                TryDelete(temp);
                return null;
            }

            File.Move(temp, target, overwrite: true);
            return target;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null; // fail-safe: never throw a thumbnail error into the UI
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
