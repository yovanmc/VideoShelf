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
    private static (PlayerViewModel vm, FakePlaybackEngine engine) Make(AppTempDb temp)
    {
        var lib = new LibraryRepository(temp.Db);
        var seriesId = lib.UpsertSeries(lib.UpsertSection(lib.UpsertSource(@"C:\V", "V"), "S"), "Base", false);
        var videoId = lib.UpsertVideo(seriesId, @"C:\V\S\a.mp4", 1, ".mp4");
        var ep = new EpisodeView(videoId, seriesId, @"C:\V\S\a.mp4", 1, "Base", false, false);
        var engine = new FakePlaybackEngine();
        var vm = new PlayerViewModel(engine, lib, new WatchRepository(temp.Db),
            new SettingsRepository(temp.Db), new ResumePolicy(), new FakeSubtitleFilePicker());
        vm.Open(ep);
        return (vm, engine);
    }

    [Fact]
    public void AppPaths_exposes_preview_dirs_under_root()
    {
        var paths = new AppPaths(@"C:\Root");

        paths.SeekPreviewDirectory.ShouldBe(@"C:\Root\seek-preview");
        paths.CoversDirectory.ShouldBe(@"C:\Root\covers");
    }

    [Fact]
    public async System.Threading.Tasks.Task SeekPreview_returns_null_on_engine_failure()
    {
        using var temp = new AppTempDb();
        using var dir = new TempDir();
        var (vm, engine) = Make(temp);
        vm.SeekPreviewDirectory = dir.Path;
        engine.SnapshotShouldFail = true;

        var path = await vm.RequestSeekPreviewAsync(12.0, System.Threading.CancellationToken.None);

        path.ShouldBeNull();
    }
}
