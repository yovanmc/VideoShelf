using System;
using System.Threading;
using Shouldly;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;
using Xunit;

namespace VideoShelf.Core.Tests.Storage;

public sealed class HistoryRepositoryTests
{
    private static (LibraryRepository lib, WatchRepository watch, long seriesId, long videoId) Seed(TempDb temp)
    {
        var lib = new LibraryRepository(temp.Db);
        var srcId = lib.UpsertSource(@"C:\V", "V");
        var secId = lib.UpsertSection(srcId, "Section");
        var seriesId = lib.UpsertSeries(secId, "Base", false);
        var videoId = lib.UpsertVideo(seriesId, @"C:\V\S\a.mp4", 1, ".mp4");
        var watch = new WatchRepository(temp.Db);
        return (lib, watch, seriesId, videoId);
    }

    [Fact]
    public void GetHistory_returns_empty_when_no_events()
    {
        using var temp = new TempDb();
        var history = new HistoryRepository(temp.Db);
        history.GetHistory(100).ShouldBeEmpty();
    }

    [Fact]
    public void GetHistory_returns_row_after_marking_watched()
    {
        using var temp = new TempDb();
        var (lib, watch, seriesId, videoId) = Seed(temp);
        watch.SetWatched(videoId, true);
        var history = new HistoryRepository(temp.Db);

        var rows = history.GetHistory(100);

        rows.Count.ShouldBe(1);
        rows[0].VideoId.ShouldBe(videoId);
        rows[0].SeriesId.ShouldBe(seriesId);
        rows[0].SeriesTitle.ShouldBe("Base");
        rows[0].EpisodeNo.ShouldBe(1);
        rows[0].IsStandalone.ShouldBeFalse();
        rows[0].WatchedAt.ShouldNotBeNullOrEmpty();
        rows[0].ThumbnailSeedPath.ShouldBeNull(); // thumbnail_path not set by UpsertVideo fixture
    }

    [Fact]
    public void GetHistory_orders_by_watched_at_descending()
    {
        using var temp = new TempDb();
        var lib = new LibraryRepository(temp.Db);
        var srcId = lib.UpsertSource(@"C:\V", "V");
        var secId = lib.UpsertSection(srcId, "S");
        var seriesId = lib.UpsertSeries(secId, "Base", false);
        var vid1 = lib.UpsertVideo(seriesId, @"C:\V\S\a.mp4", 1, ".mp4");
        var vid2 = lib.UpsertVideo(seriesId, @"C:\V\S\b.mp4", 2, ".mp4");

        var watch = new WatchRepository(temp.Db);
        watch.SetWatched(vid1, true);
        Thread.Sleep(10); // ensure distinct timestamps
        watch.SetWatched(vid2, true);

        var history = new HistoryRepository(temp.Db);
        var rows = history.GetHistory(100);

        // Most recently watched (vid2) should be first.
        rows[0].VideoId.ShouldBe(vid2);
        rows[1].VideoId.ShouldBe(vid1);
    }

    [Fact]
    public void GetHistory_respects_limit()
    {
        using var temp = new TempDb();
        var lib = new LibraryRepository(temp.Db);
        var srcId = lib.UpsertSource(@"C:\V", "V");
        var secId = lib.UpsertSection(srcId, "S");
        var seriesId = lib.UpsertSeries(secId, "Base", false);
        var watch = new WatchRepository(temp.Db);

        for (var i = 1; i <= 5; i++)
        {
            var vid = lib.UpsertVideo(seriesId, $@"C:\V\S\e{i:00}.mp4", i, ".mp4");
            watch.SetWatched(vid, true);
        }

        var history = new HistoryRepository(temp.Db);
        history.GetHistory(3).Count.ShouldBe(3);
    }

    [Fact]
    public void GetHistory_returns_multiple_events_for_same_video()
    {
        using var temp = new TempDb();
        var (_, watch, _, videoId) = Seed(temp);

        // Watch twice: unwatch in between to re-watch.
        watch.SetWatched(videoId, true);
        watch.SetWatched(videoId, false);
        watch.SetWatched(videoId, true);

        var history = new HistoryRepository(temp.Db);
        // Both watch events appear (unwatch inserts NO event).
        history.GetHistory(100).Count.ShouldBe(2);
    }

    [Fact]
    public void GetHistory_join_fields_correct_for_standalone()
    {
        using var temp = new TempDb();
        var lib = new LibraryRepository(temp.Db);
        var srcId = lib.UpsertSource(@"C:\V", "V");
        var secId = lib.UpsertSection(srcId, "S");
        var seriesId = lib.UpsertSeries(secId, "Movie", true); // standalone
        var videoId = lib.UpsertVideo(seriesId, @"C:\V\S\movie.mp4", 1, ".mp4");
        var watch = new WatchRepository(temp.Db);
        watch.SetWatched(videoId, true);

        var history = new HistoryRepository(temp.Db);
        var rows = history.GetHistory(100);

        rows.Count.ShouldBe(1);
        rows[0].IsStandalone.ShouldBeTrue();
        rows[0].SeriesTitle.ShouldBe("Movie");
    }
}
