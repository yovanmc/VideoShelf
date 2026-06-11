using Shouldly;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.Tests;

public class EpisodeViewModelTests
{
    private static (WatchRepository watch, long videoId) Seed(AppTempDb temp)
    {
        var lib = new LibraryRepository(temp.Db);
        var seriesId = lib.UpsertSeries(lib.UpsertSection(lib.UpsertSource(@"C:\V", "V"), "S"), "Base", false);
        var videoId = lib.UpsertVideo(seriesId, @"C:\V\S\a.mp4", 1, ".mp4");
        return (new WatchRepository(temp.Db), videoId);
    }

    [Fact]
    public void ToggleWatched_flips_flag_and_persists()
    {
        using var temp = new AppTempDb();
        var (watch, videoId) = Seed(temp);
        var view = new EpisodeView(videoId, 1, @"C:\V\S\a.mp4", 1, "Base", Watched: false, Missing: false);
        var vm = new EpisodeViewModel(view, watch);

        vm.ToggleWatchedCommand.Execute(null);

        vm.Watched.ShouldBeTrue();
        watch.IsWatched(videoId).ShouldBeTrue();

        vm.ToggleWatchedCommand.Execute(null);
        vm.Watched.ShouldBeFalse();
        watch.IsWatched(videoId).ShouldBeFalse();
    }

    [Fact]
    public void Missing_episode_exposes_flag_for_dimming()
    {
        using var temp = new AppTempDb();
        var (watch, videoId) = Seed(temp);
        var view = new EpisodeView(videoId, 1, @"C:\V\S\a.mp4", 1, "Base", Watched: false, Missing: true);

        var vm = new EpisodeViewModel(view, watch);

        vm.IsMissing.ShouldBeTrue();
        vm.Title.ShouldBe("Base");
    }
}
