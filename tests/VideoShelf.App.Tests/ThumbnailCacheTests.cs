using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Shouldly;
using VideoShelf.App.Services;

namespace VideoShelf.App.Tests;

public class ThumbnailCacheTests
{
    private sealed class FakeSnapshotter : IThumbnailSnapshotter
    {
        private readonly bool _succeed;
        public int Calls { get; private set; }
        public FakeSnapshotter(bool succeed) => _succeed = succeed;

        public Task<bool> TrySnapshotAsync(string videoPath, string outputPngPath, CancellationToken ct)
        {
            Calls++;
            if (_succeed)
                File.WriteAllBytes(outputPngPath, new byte[] { 1, 2, 3 });
            return Task.FromResult(_succeed);
        }
    }

    private static string TempThumbDir()
        => Path.Combine(Path.GetTempPath(), "vshelf_thumbs_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task GetThumbnail_creates_then_reuses_cached_png()
    {
        var dir = TempThumbDir();
        try
        {
            var snap = new FakeSnapshotter(succeed: true);
            var cache = new ThumbnailCache(dir, snap);

            var first = await cache.GetThumbnailPathAsync(@"C:\V\S\a.mp4", CancellationToken.None);
            var second = await cache.GetThumbnailPathAsync(@"C:\V\S\a.mp4", CancellationToken.None);

            first.ShouldNotBeNull();
            File.Exists(first!).ShouldBeTrue();
            second.ShouldBe(first);
            snap.Calls.ShouldBe(1); // second call served from cache
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task GetThumbnail_returns_null_when_snapshot_fails_and_never_throws()
    {
        var dir = TempThumbDir();
        try
        {
            var cache = new ThumbnailCache(dir, new FakeSnapshotter(succeed: false));

            var result = await cache.GetThumbnailPathAsync(@"C:\V\S\missing.mp4", CancellationToken.None);

            result.ShouldBeNull();
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void CacheKey_is_stable_and_path_dependent()
    {
        var a1 = ThumbnailCache.CacheFileName(@"C:\V\S\a.mp4");
        var a2 = ThumbnailCache.CacheFileName(@"C:\V\S\a.mp4");
        var b = ThumbnailCache.CacheFileName(@"C:\V\S\b.mp4");

        a1.ShouldBe(a2);
        a1.ShouldNotBe(b);
        a1.ShouldEndWith(".png");
    }
}
