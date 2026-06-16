using System;
using System.Threading.Tasks;
using Shouldly;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;
using Xunit;

namespace VideoShelf.App.Tests;

public class WatchLaterViewModelTests
{
    private static (CurationRepository curation, LibraryRepository lib, long videoId) Seed(AppTempDb temp)
    {
        var lib = new LibraryRepository(temp.Db);
        var ser = lib.UpsertSeries(lib.UpsertSection(lib.UpsertSource(@"C:\V", "V"), "S"), "Base", false);
        var vid = lib.UpsertVideo(ser, @"C:\V\S\a.mp4", 1, ".mp4");
        var curation = new CurationRepository(temp.Db);
        return (curation, lib, vid);
    }

    [Fact]
    public async Task LoadAsync_populates_Watchlist_when_a_video_is_in_watchlist()
    {
        using var temp = new AppTempDb();
        var (curation, lib, videoId) = Seed(temp);
        curation.SetWatchlist(videoId, true, DateTimeOffset.UtcNow);
        var vm = new WatchLaterViewModel(curation, lib);

        await vm.LoadAsync();

        vm.Watchlist.Count.ShouldBe(1);
        vm.Watchlist[0].VideoId.ShouldBe(videoId);
        vm.HasWatchlist.ShouldBeTrue();
    }

    [Fact]
    public async Task LoadAsync_produces_empty_collection_when_no_watchlist_items()
    {
        using var temp = new AppTempDb();
        var (curation, lib, _) = Seed(temp);
        var vm = new WatchLaterViewModel(curation, lib);

        await vm.LoadAsync();

        vm.Watchlist.ShouldBeEmpty();
        vm.HasWatchlist.ShouldBeFalse();
    }

    [Fact]
    public async Task Card_Play_raises_PlayRequested_with_resolved_episode()
    {
        using var temp = new AppTempDb();
        var (curation, lib, videoId) = Seed(temp);
        curation.SetWatchlist(videoId, true, DateTimeOffset.UtcNow);
        var vm = new WatchLaterViewModel(curation, lib);
        await vm.LoadAsync();

        EpisodeView? played = null;
        vm.PlayRequested += (_, ep) => played = ep;
        vm.Watchlist[0].PlayCommand.Execute(null);

        played.ShouldNotBeNull();
        played!.VideoId.ShouldBe(videoId);
    }
}
