using Shouldly;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;

namespace VideoShelf.Core.Tests.Storage;

public class StatsRepositoryTests
{
    [Fact]
    public void GetLibraryStats_counts_and_sums()
    {
        using var temp = new TempDb();
        var lib   = new LibraryRepository(temp.Db);
        var watch = new WatchRepository(temp.Db);
        var stats = new StatsRepository(temp.Db);

        // Creator A
        var srcA    = lib.UpsertSource(@"C:\A", "CreatorA");
        var secA    = lib.UpsertSection(srcA, "Section A");
        var seriesA = lib.UpsertSeries(secA, "Series A", false);
        var vid1    = lib.UpsertVideo(seriesA, @"C:\A\Section A\ep1.mp4", 1, ".mp4");
        var vid2    = lib.UpsertVideo(seriesA, @"C:\A\Section A\ep2.mp4", 2, ".mp4");

        // Creator B
        var srcB    = lib.UpsertSource(@"C:\B", "CreatorB");
        var secB    = lib.UpsertSection(srcB, "Section B");
        var seriesB = lib.UpsertSeries(secB, "Series B", false);
        var vid3    = lib.UpsertVideo(seriesB, @"C:\B\Section B\ep1.mp4", 1, ".mp4");
        var vid4    = lib.UpsertVideo(seriesB, @"C:\B\Section B\ep2.mp4", 2, ".mp4");

        // Mark vid1 and vid2 as watched with durations
        lib.SetDuration(vid1, 100.0);
        lib.SetDuration(vid2, 200.0);
        watch.SetWatched(vid1, true);
        watch.SetWatched(vid2, true);

        // Set resume position on vid3 (in-progress)
        lib.SetResumePosition(vid3, 42.0);

        var result = stats.GetLibraryStats();

        result.TotalVideos.ShouldBe(4);
        result.WatchedVideos.ShouldBe(2);
        result.InProgressVideos.ShouldBe(1);
        result.WatchedDurationSeconds.ShouldBe(300.0);
    }

    [Fact]
    public void GetTopCreatorsByWatched_orders_by_count_desc()
    {
        using var temp = new TempDb();
        var lib   = new LibraryRepository(temp.Db);
        var watch = new WatchRepository(temp.Db);
        var stats = new StatsRepository(temp.Db);

        // Creator A — 2 watched
        var srcA    = lib.UpsertSource(@"C:\A", "CreatorA");
        var secA    = lib.UpsertSection(srcA, "Section A");
        var seriesA = lib.UpsertSeries(secA, "Series A", false);
        var a1      = lib.UpsertVideo(seriesA, @"C:\A\Section A\ep1.mp4", 1, ".mp4");
        var a2      = lib.UpsertVideo(seriesA, @"C:\A\Section A\ep2.mp4", 2, ".mp4");
        watch.SetWatched(a1, true);
        watch.SetWatched(a2, true);

        // Creator B — 1 watched
        var srcB    = lib.UpsertSource(@"C:\B", "CreatorB");
        var secB    = lib.UpsertSection(srcB, "Section B");
        var seriesB = lib.UpsertSeries(secB, "Series B", false);
        var b1      = lib.UpsertVideo(seriesB, @"C:\B\Section B\ep1.mp4", 1, ".mp4");
        watch.SetWatched(b1, true);

        // Creator C — 0 watched
        var srcC    = lib.UpsertSource(@"C:\C", "CreatorC");
        var secC    = lib.UpsertSection(srcC, "Section C");
        var seriesC = lib.UpsertSeries(secC, "Series C", false);
        lib.UpsertVideo(seriesC, @"C:\C\Section C\ep1.mp4", 1, ".mp4");

        var result = stats.GetTopCreatorsByWatched(10);

        result.Count.ShouldBe(2);
        result[0].Name.ShouldBe("Section A");
        result[0].WatchedCount.ShouldBe(2);
        result[1].Name.ShouldBe("Section B");
        result[1].WatchedCount.ShouldBe(1);
    }
}
