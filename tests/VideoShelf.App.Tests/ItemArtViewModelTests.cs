using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Shouldly;
using VideoShelf.App.Services;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;

namespace VideoShelf.App.Tests;

/// <summary>
/// Tests for per-item art overrides:
/// - SeriesViewModel respects series_art over seed thumbnail
/// - PlayerViewModel.SetCoverFromFrame writes to CoversDirectory (never a library path)
/// </summary>
public class ItemArtViewModelTests
{
    // ── stub thumbnail services ────────────────────────────────────────────

    private sealed class SeedThumbs : IThumbnailService
    {
        public const string SeedResult = @"C:\thumbs\seed.png";
        public Task<string?> GetThumbnailPathAsync(string videoPath, CancellationToken ct)
            => Task.FromResult<string?>(SeedResult);
    }

    private sealed class NullThumbs : IThumbnailService
    {
        public Task<string?> GetThumbnailPathAsync(string videoPath, CancellationToken ct)
            => Task.FromResult<string?>(null);
    }

    // ── helper: seed a section with one series ─────────────────────────────

    private static (LibraryRepository lib, ItemArtRepository itemArt, SeriesSummary summary)
        SeedSeries(AppTempDb temp)
    {
        var lib = new LibraryRepository(temp.Db);
        var itemArt = new ItemArtRepository(temp.Db);
        var srcId = lib.UpsertSource(@"C:\vids", "V");
        var secId = lib.UpsertSection(srcId, "Creator A");
        var seriesId = lib.UpsertSeries(secId, "Show", false);
        lib.UpsertVideo(seriesId, @"C:\vids\Show\e01.mkv", 1, "mkv");
        var summary = lib.GetSeriesSummaries(secId).Single();
        return (lib, itemArt, summary);
    }

    // ── SeriesViewModel: series_art overrides seed thumbnail ───────────────

    [Fact]
    public async Task LoadThumbnail_uses_series_art_over_seed_when_set()
    {
        using var temp = new AppTempDb();
        var (_, itemArt, summary) = SeedSeries(temp);
        var watch = new WatchRepository(temp.Db);
        const string customCover = @"C:\covers\custom_cover.png";

        itemArt.SetSeriesArt(summary.SeriesId, customCover);

        var vm = new SeriesViewModel(summary, new LibraryRepository(temp.Db), watch,
            new SeedThumbs(), itemArt: itemArt);
        await vm.LoadThumbnailAsync(CancellationToken.None);

        // The custom cover must win over the seed-based thumbnail.
        vm.ThumbnailPath.ShouldBe(customCover);
        vm.ThumbnailPath.ShouldNotBe(SeedThumbs.SeedResult);
    }

    [Fact]
    public async Task LoadThumbnail_falls_back_to_seed_when_no_series_art()
    {
        using var temp = new AppTempDb();
        var (_, itemArt, summary) = SeedSeries(temp);
        var watch = new WatchRepository(temp.Db);

        // No series_art set — should fall back to seed-based thumbnail.
        var vm = new SeriesViewModel(summary, new LibraryRepository(temp.Db), watch,
            new SeedThumbs(), itemArt: itemArt);
        await vm.LoadThumbnailAsync(CancellationToken.None);

        vm.ThumbnailPath.ShouldBe(SeedThumbs.SeedResult);
    }

    [Fact]
    public async Task LoadThumbnail_falls_back_to_seed_after_series_art_cleared()
    {
        using var temp = new AppTempDb();
        var (_, itemArt, summary) = SeedSeries(temp);
        var watch = new WatchRepository(temp.Db);

        itemArt.SetSeriesArt(summary.SeriesId, @"C:\covers\custom_cover.png");
        itemArt.ClearSeriesArt(summary.SeriesId);

        var vm = new SeriesViewModel(summary, new LibraryRepository(temp.Db), watch,
            new SeedThumbs(), itemArt: itemArt);
        await vm.LoadThumbnailAsync(CancellationToken.None);

        vm.ThumbnailPath.ShouldBe(SeedThumbs.SeedResult);
    }

    // ── PlayerViewModel: SetCoverFromFrame ─────────────────────────────────

    private static (PlayerViewModel vm, FakePlaybackEngine engine, ItemArtRepository itemArt,
        long videoId, string libraryPath)
        MakePlayer(AppTempDb temp, string coversDir, TempDir libraryDir)
    {
        var lib = new LibraryRepository(temp.Db);
        // Library folder under a real temp dir so File.Exists passes in Open().
        // The test asserts the cover is NOT written under this directory.
        var libraryPath = libraryDir.Path;
        var episodePath = libraryDir.Touch("Show/e01.mp4");
        var srcId = lib.UpsertSource(libraryPath, "Lib");
        var secId = lib.UpsertSection(srcId, "Creator");
        var seriesId = lib.UpsertSeries(secId, "Show", false);
        var videoId = lib.UpsertVideo(seriesId, episodePath, 1, "mp4");

        var itemArt = new ItemArtRepository(temp.Db);
        var engine = new FakePlaybackEngine();
        var vm = new PlayerViewModel(engine, lib, new WatchRepository(temp.Db),
            new SettingsRepository(temp.Db), new ResumePolicy(), new FakeSubtitleFilePicker(),
            itemArt)
        {
            CoversDirectory = coversDir,
        };
        var ep = new EpisodeView(videoId, seriesId, episodePath, 1, "Show", false, false);
        vm.Open(ep);
        return (vm, engine, itemArt, videoId, libraryPath);
    }

    [Fact]
    public void SetCoverFromFrame_writes_to_covers_dir_not_library()
    {
        using var temp = new AppTempDb();
        using var coversDir = new TempDir();
        using var libraryDir = new TempDir();

        var (vm, _, itemArt, videoId, libraryPath) = MakePlayer(temp, coversDir.Path, libraryDir);

        vm.SetCoverFromFrameCommand.Execute(null);

        // Art path must be set and under coversDir, NOT under the library folder.
        var artPath = itemArt.GetVideoArt(videoId);
        artPath.ShouldNotBeNull();
        artPath!.ShouldStartWith(coversDir.Path);
        artPath.ShouldNotContain(libraryPath);
    }

    [Fact]
    public void SetCoverFromFrame_stores_path_under_covers_dir()
    {
        using var temp = new AppTempDb();
        using var coversDir = new TempDir();
        using var libraryDir = new TempDir();

        var (vm, _, itemArt, videoId, _) = MakePlayer(temp, coversDir.Path, libraryDir);

        vm.SetCoverFromFrameCommand.Execute(null);

        var artPath = itemArt.GetVideoArt(videoId);
        artPath.ShouldNotBeNull();
        Path.GetDirectoryName(artPath).ShouldBe(coversDir.Path);
        Path.GetFileName(artPath).ShouldBe($"cover_{videoId}.png");
    }

    [Fact]
    public void SetCoverFromFrame_no_op_when_engine_fails()
    {
        using var temp = new AppTempDb();
        using var coversDir = new TempDir();
        using var libraryDir = new TempDir();

        var (vm, engine, itemArt, videoId, _) = MakePlayer(temp, coversDir.Path, libraryDir);
        engine.SnapshotShouldFail = true;

        vm.SetCoverFromFrameCommand.Execute(null); // must not throw

        itemArt.GetVideoArt(videoId).ShouldBeNull();
    }

    [Fact]
    public void SetCoverFromFrame_no_op_when_no_itemArt_repo()
    {
        using var temp = new AppTempDb();
        using var coversDir = new TempDir();

        // Build PlayerViewModel without itemArt (null)
        var lib = new LibraryRepository(temp.Db);
        var srcId = lib.UpsertSource(@"C:\V", "V");
        var secId = lib.UpsertSection(srcId, "S");
        var seriesId = lib.UpsertSeries(secId, "Show", false);
        var videoId = lib.UpsertVideo(seriesId, @"C:\V\Show\e01.mp4", 1, "mp4");
        var engine = new FakePlaybackEngine();
        var vm = new PlayerViewModel(engine, lib, new WatchRepository(temp.Db),
            new SettingsRepository(temp.Db), new ResumePolicy(), new FakeSubtitleFilePicker())
        {
            CoversDirectory = coversDir.Path,
        };
        var ep = new EpisodeView(videoId, seriesId, @"C:\V\Show\e01.mp4", 1, "Show", false, false);
        vm.Open(ep);

        vm.SetCoverFromFrameCommand.Execute(null); // must not throw

        engine.SnapshotCount.ShouldBe(0); // no snapshot attempted without itemArt
    }
}
