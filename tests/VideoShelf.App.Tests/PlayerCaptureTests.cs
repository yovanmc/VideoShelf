using System.IO;
using Shouldly;
using VideoShelf.App.Services;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;

namespace VideoShelf.App.Tests;

public class PlayerCaptureTests
{
    private static (PlayerViewModel vm, FakePlaybackEngine engine) Make(AppTempDb temp, string captureDir)
    {
        var lib = new LibraryRepository(temp.Db);
        var seriesId = lib.UpsertSeries(lib.UpsertSection(lib.UpsertSource(@"C:\V", "V"), "S"), "Base", false);
        var videoId = lib.UpsertVideo(seriesId, @"C:\V\S\a.mp4", 1, ".mp4");
        var ep = new EpisodeView(videoId, seriesId, @"C:\V\S\a.mp4", 1, "Base", false, false);
        var engine = new FakePlaybackEngine();
        var vm = new PlayerViewModel(engine, lib, new WatchRepository(temp.Db),
            new SettingsRepository(temp.Db), new ResumePolicy())
        {
            CaptureDirectory = captureDir,
        };
        vm.Open(ep);
        return (vm, engine);
    }

    [Fact]
    public void AppPaths_exposes_capture_and_preview_dirs_under_root()
    {
        var paths = new AppPaths(@"C:\Root");

        paths.CaptureDirectory.ShouldBe(@"C:\Root\captures");
        paths.SeekPreviewDirectory.ShouldBe(@"C:\Root\seek-preview");
    }

    [Fact]
    public void Screenshot_invokes_engine_snapshot_into_capture_dir()
    {
        using var temp = new AppTempDb();
        using var dir = new TempDir();
        var (vm, engine) = Make(temp, dir.Path);

        vm.ScreenshotCommand.Execute(null);

        engine.SnapshotCount.ShouldBe(1);
        vm.LastScreenshotPath.ShouldNotBeNull();
        Path.GetDirectoryName(vm.LastScreenshotPath!).ShouldBe(dir.Path);
    }

    [Fact]
    public void Screenshot_failure_is_swallowed_and_path_stays_null()
    {
        using var temp = new AppTempDb();
        using var dir = new TempDir();
        var (vm, engine) = Make(temp, dir.Path);
        engine.SnapshotShouldFail = true;

        vm.ScreenshotCommand.Execute(null); // must not throw

        vm.LastScreenshotPath.ShouldBeNull();
    }

    [Fact]
    public async System.Threading.Tasks.Task SeekPreview_returns_null_on_engine_failure()
    {
        using var temp = new AppTempDb();
        using var dir = new TempDir();
        var (vm, engine) = Make(temp, dir.Path);
        vm.SeekPreviewDirectory = dir.Path;
        engine.SnapshotShouldFail = true;

        var path = await vm.RequestSeekPreviewAsync(12.0, System.Threading.CancellationToken.None);

        path.ShouldBeNull();
    }
}
