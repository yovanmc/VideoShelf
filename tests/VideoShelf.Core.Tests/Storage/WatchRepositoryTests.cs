using System.Linq;
using Shouldly;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;

namespace VideoShelf.Core.Tests.Storage;

public class WatchRepositoryTests
{
    private static long SeedVideo(TempDb temp)
    {
        var lib = new LibraryRepository(temp.Db);
        var seriesId = lib.UpsertSeries(lib.UpsertSection(lib.UpsertSource(@"C:\V", "V"), "S"), "Base", false);
        return lib.UpsertVideo(seriesId, @"C:\V\S\a.mp4", 1, ".mp4");
    }

    [Fact]
    public void MarkWatched_sets_flag_and_records_event()
    {
        using var temp = new TempDb();
        var videoId = SeedVideo(temp);
        var watch = new WatchRepository(temp.Db);

        watch.SetWatched(videoId, true);

        watch.IsWatched(videoId).ShouldBeTrue();
        watch.RecentlyWatchedVideoIds(10).ShouldContain(videoId);
    }

    [Fact]
    public void Toggle_unwatched_clears_flag_but_keeps_history()
    {
        using var temp = new TempDb();
        var videoId = SeedVideo(temp);
        var watch = new WatchRepository(temp.Db);

        watch.SetWatched(videoId, true);
        watch.SetWatched(videoId, false);

        watch.IsWatched(videoId).ShouldBeFalse();
        watch.RecentlyWatchedVideoIds(10).ShouldContain(videoId); // event history retained
    }

    [Fact]
    public void MarkWatched_clears_resume_position()
    {
        using var temp = new TempDb();
        var videoId = SeedVideo(temp);
        var lib = new LibraryRepository(temp.Db);
        var watch = new WatchRepository(temp.Db);
        lib.SetResumePosition(videoId, 55.0);

        watch.SetWatched(videoId, true);

        lib.GetResumePosition(videoId).ShouldBeNull();
    }

    [Fact]
    public void MarkUnwatched_does_not_touch_resume_position()
    {
        using var temp = new TempDb();
        var videoId = SeedVideo(temp);
        var lib = new LibraryRepository(temp.Db);
        var watch = new WatchRepository(temp.Db);
        lib.SetResumePosition(videoId, 30.0);

        watch.SetWatched(videoId, false);

        lib.GetResumePosition(videoId).ShouldBe(30.0);
    }
}
