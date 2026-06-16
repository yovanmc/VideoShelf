using System;
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

    // ── E1: GetRatingDistribution ─────────────────────────────────────────────

    [Fact]
    public void GetRatingDistribution_returns_empty_on_empty_library()
    {
        using var temp = new TempDb();
        var stats = new StatsRepository(temp.Db);

        var result = stats.GetRatingDistribution();

        result.ShouldBeEmpty();
    }

    [Fact]
    public void GetRatingDistribution_groups_by_rating_value()
    {
        using var temp = new TempDb();
        var lib   = new LibraryRepository(temp.Db);
        var curation = new CurationRepository(temp.Db);
        var stats = new StatsRepository(temp.Db);

        var src     = lib.UpsertSource(@"C:\R", "R");
        var sec     = lib.UpsertSection(src, "Sec");
        var series  = lib.UpsertSeries(sec, "Ser", false);
        var vid1    = lib.UpsertVideo(series, @"C:\R\Sec\e1.mp4", 1, ".mp4");
        var vid2    = lib.UpsertVideo(series, @"C:\R\Sec\e2.mp4", 2, ".mp4");
        var vid3    = lib.UpsertVideo(series, @"C:\R\Sec\e3.mp4", 3, ".mp4");

        // vid1 = 4.5, vid2 = 4.5, vid3 = 0 (unrated)
        curation.SetRating(vid1, 4.5);
        curation.SetRating(vid2, 4.5);
        // vid3 left at default 0

        var result = stats.GetRatingDistribution();

        // Should have two buckets: 0 (1 video) and 4.5 (2 videos)
        result.Count.ShouldBe(2);
        var zeroBucket = result.First(b => b.Rating == 0.0);
        zeroBucket.Count.ShouldBe(1);
        var highBucket = result.First(b => b.Rating == 4.5);
        highBucket.Count.ShouldBe(2);
    }

    // ── E1: GetWatchActivityByMonth ───────────────────────────────────────────

    [Fact]
    public void GetWatchActivityByMonth_returns_empty_on_empty_library()
    {
        using var temp = new TempDb();
        var stats = new StatsRepository(temp.Db);

        var result = stats.GetWatchActivityByMonth(12);

        result.ShouldBeEmpty();
    }

    [Fact]
    public void GetWatchActivityByMonth_counts_events_in_current_month()
    {
        using var temp = new TempDb();
        var lib   = new LibraryRepository(temp.Db);
        var watch = new WatchRepository(temp.Db);
        var stats = new StatsRepository(temp.Db);

        var src    = lib.UpsertSource(@"C:\W", "W");
        var sec    = lib.UpsertSection(src, "Sec");
        var series = lib.UpsertSeries(sec, "Ser", false);
        var vid1   = lib.UpsertVideo(series, @"C:\W\Sec\e1.mp4", 1, ".mp4");
        var vid2   = lib.UpsertVideo(series, @"C:\W\Sec\e2.mp4", 2, ".mp4");

        watch.SetWatched(vid1, true);
        watch.SetWatched(vid2, true);

        var result = stats.GetWatchActivityByMonth(12);

        // At least one period with count >= 2
        result.ShouldNotBeEmpty();
        result.Sum(p => p.Count).ShouldBeGreaterThanOrEqualTo(2);
    }

    // ── E1: GetTopTagsByWatch ─────────────────────────────────────────────────

    [Fact]
    public void GetTopTagsByWatch_returns_empty_when_no_video_tags_exist()
    {
        using var temp = new TempDb();
        var stats = new StatsRepository(temp.Db);

        var result = stats.GetTopTagsByWatch(10);

        result.ShouldBeEmpty();
    }

    [Fact]
    public void GetTopTagsByWatch_counts_total_and_watched_per_tag()
    {
        using var temp = new TempDb();
        var lib   = new LibraryRepository(temp.Db);
        var watch = new WatchRepository(temp.Db);
        var tags  = new TagRepository(temp.Db);
        var stats = new StatsRepository(temp.Db);

        var src    = lib.UpsertSource(@"C:\T", "T");
        var sec    = lib.UpsertSection(src, "Sec");
        var series = lib.UpsertSeries(sec, "Ser", false);
        var vid1   = lib.UpsertVideo(series, @"C:\T\Sec\e1.mp4", 1, ".mp4");
        var vid2   = lib.UpsertVideo(series, @"C:\T\Sec\e2.mp4", 2, ".mp4");
        var vid3   = lib.UpsertVideo(series, @"C:\T\Sec\e3.mp4", 3, ".mp4");

        // Tag "action": vid1, vid2, vid3  — vid1+vid2 watched
        tags.AddVideoTag(vid1, "action");
        tags.AddVideoTag(vid2, "action");
        tags.AddVideoTag(vid3, "action");
        watch.SetWatched(vid1, true);
        watch.SetWatched(vid2, true);

        // Tag "drama": vid3 only, unwatched
        tags.AddVideoTag(vid3, "drama");

        var result = stats.GetTopTagsByWatch(10);

        result.Count.ShouldBe(2);
        var action = result.First(t => t.Tag == "action");
        action.Total.ShouldBe(3);
        action.Watched.ShouldBe(2);
        var drama = result.First(t => t.Tag == "drama");
        drama.Total.ShouldBe(1);
        drama.Watched.ShouldBe(0);
    }

    [Fact]
    public void GetTopTagsByWatch_respects_limit()
    {
        using var temp = new TempDb();
        var lib   = new LibraryRepository(temp.Db);
        var tags  = new TagRepository(temp.Db);
        var stats = new StatsRepository(temp.Db);

        var src    = lib.UpsertSource(@"C:\L", "L");
        var sec    = lib.UpsertSection(src, "Sec");
        var series = lib.UpsertSeries(sec, "Ser", false);
        for (var i = 1; i <= 5; i++)
        {
            var vid = lib.UpsertVideo(series, $@"C:\L\Sec\e{i}.mp4", i, ".mp4");
            tags.AddVideoTag(vid, $"tag{i}");
        }

        var result = stats.GetTopTagsByWatch(3);

        result.Count.ShouldBe(3);
    }

    // ── E1: GetLibraryComposition ─────────────────────────────────────────────

    [Fact]
    public void GetLibraryComposition_returns_zeros_on_empty_library()
    {
        using var temp = new TempDb();
        var stats = new StatsRepository(temp.Db);

        var result = stats.GetLibraryComposition();

        result.Creators.ShouldBe(0);
        result.Series.ShouldBe(0);
        result.Standalones.ShouldBe(0);
        result.TotalVideos.ShouldBe(0);
        result.TotalDurationSeconds.ShouldBe(0.0);
    }

    [Fact]
    public void GetLibraryComposition_counts_correctly()
    {
        using var temp = new TempDb();
        var lib   = new LibraryRepository(temp.Db);
        var stats = new StatsRepository(temp.Db);

        // Creator A: 1 series + 1 standalone
        var srcA  = lib.UpsertSource(@"C:\A", "A");
        var secA  = lib.UpsertSection(srcA, "SecA");
        var ser1  = lib.UpsertSeries(secA, "Series1", isStandalone: false);
        var sa1   = lib.UpsertSeries(secA, "StandaloneA", isStandalone: true);
        var v1    = lib.UpsertVideo(ser1, @"C:\A\SecA\e1.mp4", 1, ".mp4");
        lib.UpsertVideo(sa1, @"C:\A\SecA\s1.mp4", 1, ".mp4");

        // Creator B: 1 standalone only
        var srcB  = lib.UpsertSource(@"C:\B", "B");
        var secB  = lib.UpsertSection(srcB, "SecB");
        var sb1   = lib.UpsertSeries(secB, "StandaloneB", isStandalone: true);
        lib.UpsertVideo(sb1, @"C:\B\SecB\s1.mp4", 1, ".mp4");

        lib.SetDuration(v1, 120.0);

        var result = stats.GetLibraryComposition();

        result.Creators.ShouldBe(2);      // secA + secB
        result.Series.ShouldBe(1);        // ser1 only (is_standalone=0)
        result.Standalones.ShouldBe(2);   // sa1 + sb1
        result.TotalVideos.ShouldBe(3);
        result.TotalDurationSeconds.ShouldBe(120.0);
    }
}
