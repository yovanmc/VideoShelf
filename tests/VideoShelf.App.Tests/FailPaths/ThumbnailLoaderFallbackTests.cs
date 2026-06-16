using System;
using System.IO;
using Shouldly;
using VideoShelf.App.Services;
using Xunit;

namespace VideoShelf.App.Tests.FailPaths;

/// <summary>
/// C2 — PooledBitmapLoader.Load must always fall back to null (caller shows a neutral glyph)
/// and never throw, for every bad input: corrupt bytes, zero-byte, and missing path.
/// Pins the existing fail-safe guard + try/catch around the WPF decode.
/// </summary>
public class ThumbnailLoaderFallbackTests
{
    private static string TempFile(byte[] bytes)
    {
        var p = Path.Combine(Path.GetTempPath(), "vshelf_c2_" + Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllBytes(p, bytes);
        return p;
    }

    [Fact]
    public void Load_corrupt_random_bytes_returns_null_and_does_not_throw()
    {
        var rng = new Random(1234);
        var junk = new byte[2048];
        rng.NextBytes(junk);
        var path = TempFile(junk);
        try
        {
            var loader = new PooledBitmapLoader();
            ImageSourceOrNull(loader, path).ShouldBeNull();
            // Second call (would hit the LRU path if anything cached) must also be safe.
            ImageSourceOrNull(loader, path).ShouldBeNull();
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void Load_zero_byte_file_returns_null_and_does_not_throw()
    {
        var path = TempFile(Array.Empty<byte>());
        try
        {
            var loader = new PooledBitmapLoader();
            ImageSourceOrNull(loader, path).ShouldBeNull();
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void Load_missing_path_returns_null_and_does_not_throw()
    {
        var loader = new PooledBitmapLoader();
        var missing = Path.Combine(Path.GetTempPath(), "vshelf_c2_missing_" + Guid.NewGuid().ToString("N") + ".png");
        File.Exists(missing).ShouldBeFalse();
        ImageSourceOrNull(loader, missing).ShouldBeNull();
    }

    [Fact]
    public void Load_null_or_empty_path_returns_null_and_does_not_throw()
    {
        var loader = new PooledBitmapLoader();
        ImageSourceOrNull(loader, null).ShouldBeNull();
        ImageSourceOrNull(loader, "").ShouldBeNull();
    }

    /// <summary>Runs Load on an STA thread (WPF BitmapImage requires STA) and asserts it never throws,
    /// returning whatever it produced (expected null for all bad inputs).</summary>
    private static System.Windows.Media.ImageSource? ImageSourceOrNull(PooledBitmapLoader loader, string? path)
    {
        System.Windows.Media.ImageSource? result = null;
        Exception? thrown = null;
        var t = new System.Threading.Thread(() =>
        {
            try { result = loader.Load(path, 200); }
            catch (Exception ex) { thrown = ex; }
        });
        t.SetApartmentState(System.Threading.ApartmentState.STA);
        t.Start();
        t.Join();
        thrown.ShouldBeNull(); // fail-safe: Load NEVER throws
        return result;
    }
}
