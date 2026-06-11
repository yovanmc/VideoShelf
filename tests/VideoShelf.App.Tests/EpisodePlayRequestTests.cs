using Shouldly;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.Tests;

public class EpisodePlayRequestTests
{
    [Fact]
    public void PlayCommand_raises_PlayRequested_with_model()
    {
        using var temp = new AppTempDb();
        var lib = new LibraryRepository(temp.Db);
        var seriesId = lib.UpsertSeries(lib.UpsertSection(lib.UpsertSource(@"C:\V", "V"), "S"), "Base", false);
        var videoId = lib.UpsertVideo(seriesId, @"C:\V\S\a.mp4", 1, ".mp4");
        var view = new EpisodeView(videoId, seriesId, @"C:\V\S\a.mp4", 1, "Base", false, false);
        var vm = new EpisodeViewModel(view, new WatchRepository(temp.Db));

        EpisodeView? requested = null;
        vm.PlayRequested += (_, e) => requested = e;
        vm.PlayCommand.Execute(null);

        requested.ShouldNotBeNull();
        requested!.VideoId.ShouldBe(videoId);
        requested.FilePath.ShouldBe(@"C:\V\S\a.mp4");
    }
}
