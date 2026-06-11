using Shouldly;
using VideoShelf.App.Services;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.Tests;

public class PlayerMissingFileTests
{
    private static PlayerViewModel Vm(AppTempDb temp, FakePlaybackEngine engine, out EpisodeView ep, bool missingFlag)
    {
        var lib = new LibraryRepository(temp.Db);
        var seriesId = lib.UpsertSeries(lib.UpsertSection(lib.UpsertSource(@"C:\V", "V"), "S"), "Base", false);
        var path = @"C:\V\S\does-not-exist.mp4";
        var videoId = lib.UpsertVideo(seriesId, path, 1, ".mp4");
        ep = new EpisodeView(videoId, seriesId, path, 1, "Base", Watched: false, Missing: missingFlag);
        return new PlayerViewModel(engine, lib, new WatchRepository(temp.Db),
            new SettingsRepository(temp.Db), new ResumePolicy());
    }

    [Fact]
    public void Open_missing_file_sets_error_and_does_not_load_engine()
    {
        using var temp = new AppTempDb();
        var engine = new FakePlaybackEngine();
        var vm = Vm(temp, engine, out var ep, missingFlag: true);

        vm.Open(ep);

        vm.PlaybackError.ShouldNotBeNullOrEmpty();
        engine.LoadedPath.ShouldBeNull();
        engine.IsPlaying.ShouldBeFalse();
    }

    [Fact]
    public void Open_nonexistent_file_path_sets_error_even_if_flag_clear()
    {
        using var temp = new AppTempDb();
        var engine = new FakePlaybackEngine();
        var vm = Vm(temp, engine, out var ep, missingFlag: false);

        vm.Open(ep); // file truly does not exist on disk

        vm.PlaybackError.ShouldNotBeNullOrEmpty();
        engine.LoadedPath.ShouldBeNull();
    }
}
