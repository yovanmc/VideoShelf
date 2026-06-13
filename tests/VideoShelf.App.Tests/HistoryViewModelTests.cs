using System.Threading.Tasks;
using Shouldly;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;
using Xunit;

namespace VideoShelf.App.Tests;

public sealed class HistoryViewModelTests
{
    private static (HistoryRepository history, LibraryRepository lib, WatchRepository watch, long videoId) Seed(AppTempDb temp)
    {
        var lib = new LibraryRepository(temp.Db);
        var seriesId = lib.UpsertSeries(lib.UpsertSection(lib.UpsertSource(@"C:\V", "V"), "S"), "Base", false);
        var videoId = lib.UpsertVideo(seriesId, @"C:\V\S\a.mp4", 1, ".mp4");
        var watch = new WatchRepository(temp.Db);
        var history = new HistoryRepository(temp.Db);
        return (history, lib, watch, videoId);
    }

    [Fact]
    public async Task LoadAsync_empty_when_no_events()
    {
        using var temp = new AppTempDb();
        var (history, lib, _, _) = Seed(temp);
        var vm = new HistoryViewModel(history, lib);

        await vm.LoadAsync();

        vm.Entries.ShouldBeEmpty();
        vm.HasHistory.ShouldBeFalse();
    }

    [Fact]
    public async Task LoadAsync_populates_entries_after_watch_event()
    {
        using var temp = new AppTempDb();
        var (history, lib, watch, videoId) = Seed(temp);
        watch.SetWatched(videoId, true);
        var vm = new HistoryViewModel(history, lib);

        await vm.LoadAsync();

        vm.Entries.Count.ShouldBe(1);
        vm.Entries[0].VideoId.ShouldBe(videoId);
        vm.HasHistory.ShouldBeTrue();
    }

    [Fact]
    public async Task HistoryRowViewModel_Play_raises_PlayRequested_with_resolved_episode()
    {
        using var temp = new AppTempDb();
        var (history, lib, watch, videoId) = Seed(temp);
        watch.SetWatched(videoId, true);
        var vm = new HistoryViewModel(history, lib);
        await vm.LoadAsync();

        EpisodeView? played = null;
        vm.PlayRequested += (_, ep) => played = ep;
        vm.Entries[0].PlayCommand.Execute(null);

        played.ShouldNotBeNull();
        played!.VideoId.ShouldBe(videoId);
    }

    [Fact]
    public async Task HistoryRowViewModel_Title_episode_format()
    {
        using var temp = new AppTempDb();
        var lib = new LibraryRepository(temp.Db);
        var seriesId = lib.UpsertSeries(lib.UpsertSection(lib.UpsertSource(@"C:\V", "V"), "S"), "MySeries", false);
        var videoId = lib.UpsertVideo(seriesId, @"C:\V\S\a.mp4", 3, ".mp4");
        var watch = new WatchRepository(temp.Db);
        watch.SetWatched(videoId, true);
        var history = new HistoryRepository(temp.Db);
        var vm = new HistoryViewModel(history, lib);

        await vm.LoadAsync();

        vm.Entries[0].Title.ShouldBe("MySeries · Episode 3");
    }

    [Fact]
    public async Task HistoryRowViewModel_Title_standalone_format()
    {
        using var temp = new AppTempDb();
        var lib = new LibraryRepository(temp.Db);
        var seriesId = lib.UpsertSeries(lib.UpsertSection(lib.UpsertSource(@"C:\V", "V"), "S"), "TheMovie", true);
        var videoId = lib.UpsertVideo(seriesId, @"C:\V\S\movie.mp4", 1, ".mp4");
        var watch = new WatchRepository(temp.Db);
        watch.SetWatched(videoId, true);
        var history = new HistoryRepository(temp.Db);
        var vm = new HistoryViewModel(history, lib);

        await vm.LoadAsync();

        vm.Entries[0].Title.ShouldBe("TheMovie");
    }

    [Fact]
    public async Task Load_sync_wrapper_populates_entries()
    {
        using var temp = new AppTempDb();
        var (history, lib, watch, videoId) = Seed(temp);
        watch.SetWatched(videoId, true);
        var vm = new HistoryViewModel(history, lib);

        vm.Load();
        // Poll until the async continuation populates Entries (up to 5 s).
        var populated = await TestWait.UntilAsync(() => vm.Entries.Count >= 1);
        populated.ShouldBeTrue("HistoryViewModel.Load() async continuation did not populate Entries within the timeout");

        vm.Entries.Count.ShouldBeGreaterThanOrEqualTo(1);
    }
}
