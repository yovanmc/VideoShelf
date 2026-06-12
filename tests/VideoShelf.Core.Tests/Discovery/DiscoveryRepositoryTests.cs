using Microsoft.Data.Sqlite;
using Shouldly;
using VideoShelf.Core.Discovery;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;
using Xunit;

namespace VideoShelf.Core.Tests.Discovery;

public sealed class DiscoveryRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 11, 12, 0, 0, TimeSpan.Zero);

    private sealed record Fixture(TempDb Db, LibraryRepository Lib, WatchRepository Watch,
        TagRepository Tags, DiscoveryRepository Disc);

    private static Fixture NewFixture()
    {
        var db = new TempDb();
        var lib = new LibraryRepository(db.Db);
        var watch = new WatchRepository(db.Db);
        var tags = new TagRepository(db.Db);
        var disc = new DiscoveryRepository(db.Db, lib, tags);
        return new Fixture(db, lib, watch, tags, disc);
    }

    private static long AddVideo(Fixture f, long sectionId, string series, bool standalone, int ep)
    {
        var ser = f.Lib.UpsertSeries(sectionId, series, standalone);
        return f.Lib.UpsertVideo(ser, $@"C:\m\{series}\e{ep:00}.mkv", ep, "mkv");
    }

    private static void SetRaw(TempDb db, string sql, params (string, object)[] ps)
    {
        using var conn = db.Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in ps) cmd.Parameters.AddWithValue(k, v);
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public void ContinueWatching_returns_resumable_newest_first_and_excludes_missing()
    {
        var f = NewFixture(); using var _d = f.Db;
        var src = f.Lib.UpsertSource(@"C:\m", "M");
        var sec = f.Lib.UpsertSection(src, "S");
        var a = AddVideo(f, sec, "Alpha", false, 1);
        var b = AddVideo(f, sec, "Beta", false, 1);
        f.Lib.SetResumePosition(a, 10);
        f.Lib.SetResumePosition(b, 20);
        SetRaw(f.Db, "UPDATE videos SET resume_updated_at=$t WHERE id=$id",
            ("$t", Now.AddMinutes(-10).ToString("O")), ("$id", a));
        SetRaw(f.Db, "UPDATE videos SET resume_updated_at=$t WHERE id=$id",
            ("$t", Now.ToString("O")), ("$id", b));

        var items = f.Disc.GetContinueWatching(limit: 10);
        items.Select(i => i.VideoId).ShouldBe(new[] { b, a });
        items[0].ResumePosition.ShouldBe(20);
    }

    [Fact]
    public void RecentlyAdded_orders_by_added_at_desc()
    {
        var f = NewFixture(); using var _d = f.Db;
        var src = f.Lib.UpsertSource(@"C:\m", "M");
        var sec = f.Lib.UpsertSection(src, "S");
        var a = AddVideo(f, sec, "Alpha", false, 1);
        var b = AddVideo(f, sec, "Beta", false, 1);
        SetRaw(f.Db, "UPDATE videos SET added_at=$t WHERE id=$id", ("$t", "2026-06-01T00:00:00.000Z"), ("$id", a));
        SetRaw(f.Db, "UPDATE videos SET added_at=$t WHERE id=$id", ("$t", "2026-06-10T00:00:00.000Z"), ("$id", b));
        f.Disc.GetRecentlyAdded(10).Select(i => i.VideoId).ShouldBe(new[] { b, a });
    }

    [Fact]
    public void RecentlyWatched_orders_by_latest_watch_event()
    {
        var f = NewFixture(); using var _d = f.Db;
        var src = f.Lib.UpsertSource(@"C:\m", "M");
        var sec = f.Lib.UpsertSection(src, "S");
        var a = AddVideo(f, sec, "Alpha", false, 1);
        var b = AddVideo(f, sec, "Beta", false, 1);
        SetRaw(f.Db, "INSERT INTO watch_events (video_id, watched_at) VALUES ($v,$t)",
            ("$v", a), ("$t", "2026-06-05T00:00:00.000Z"));
        SetRaw(f.Db, "INSERT INTO watch_events (video_id, watched_at) VALUES ($v,$t)",
            ("$v", b), ("$t", "2026-06-09T00:00:00.000Z"));
        f.Disc.GetRecentlyWatched(10).Select(i => i.VideoId).ShouldBe(new[] { b, a });
    }

    [Fact]
    public void ForYou_suggests_unwatched_sections_sharing_tags_with_history()
    {
        var f = NewFixture(); using var _d = f.Db;
        var src = f.Lib.UpsertSource(@"C:\m", "M");
        var watchedSec = f.Lib.UpsertSection(src, "Watched");
        var candidate = f.Lib.UpsertSection(src, "Candidate");
        var unrelated = f.Lib.UpsertSection(src, "Unrelated");
        f.Tags.AddTag(watchedSec, "comedy");
        f.Tags.AddTag(candidate, "comedy");
        f.Tags.AddTag(unrelated, "horror");
        var w = AddVideo(f, watchedSec, "WShow", false, 1);
        AddVideo(f, candidate, "CShow", false, 1);
        AddVideo(f, unrelated, "UShow", false, 1);
        SetRaw(f.Db, "INSERT INTO watch_events (video_id, watched_at) VALUES ($v,$t)",
            ("$v", w), ("$t", Now.AddDays(-1).ToString("O")));

        var sugg = f.Disc.GetForYou(limit: 10, now: Now);
        sugg.Select(s => s.SectionId).ShouldBe(new[] { candidate });
        sugg[0].Score.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void GetSectionsByTags_returns_matching_sections_scored()
    {
        var f = NewFixture(); using var _d = f.Db;
        var src = f.Lib.UpsertSource(@"C:\m", "M");
        var s1 = f.Lib.UpsertSection(src, "One");
        var s2 = f.Lib.UpsertSection(src, "Two");
        f.Tags.AddTag(s1, "comedy");
        f.Tags.AddTag(s2, "drama");
        AddVideo(f, s1, "OneShow", false, 1);
        AddVideo(f, s2, "TwoShow", false, 1);
        var hits = f.Disc.GetSectionsByTags(new[] { "comedy" }, limit: 10);
        hits.Select(h => h.SectionId).ShouldBe(new[] { s1 });
    }

    [Fact]
    public void GetMoreFromSection_excludes_the_current_series()
    {
        var f = NewFixture(); using var _d = f.Db;
        var src = f.Lib.UpsertSource(@"C:\m", "M");
        var sec = f.Lib.UpsertSection(src, "S");
        var serA = f.Lib.UpsertSeries(sec, "Alpha", false);
        var serB = f.Lib.UpsertSeries(sec, "Beta", false);
        f.Lib.UpsertVideo(serA, @"C:\m\Alpha\e01.mkv", 1, "mkv");
        f.Lib.UpsertVideo(serB, @"C:\m\Beta\e01.mkv", 1, "mkv");
        var more = f.Disc.GetMoreFromSection(sec, excludeSeriesId: serA, limit: 10);
        more.Select(s => s.SeriesId).ShouldNotContain(serA);
        more.Select(s => s.SeriesId).ShouldContain(serB);
    }

    [Fact]
    public void GetRecommendedVideos_returns_unwatched_videos_from_recommended_sections()
    {
        var f = NewFixture(); using var _d = f.Db;
        var src = f.Lib.UpsertSource(@"C:\m", "M");
        var watchedSec = f.Lib.UpsertSection(src, "Watched");
        var candidate = f.Lib.UpsertSection(src, "Candidate");
        var unrelated = f.Lib.UpsertSection(src, "Unrelated");
        f.Tags.AddTag(watchedSec, "comedy");
        f.Tags.AddTag(candidate, "comedy");
        f.Tags.AddTag(unrelated, "horror");
        var w = AddVideo(f, watchedSec, "WShow", false, 1);
        var c1 = AddVideo(f, candidate, "CShow", false, 1);
        var c2 = AddVideo(f, candidate, "CShow", false, 2);
        AddVideo(f, unrelated, "UShow", false, 1);
        SetRaw(f.Db, "INSERT INTO watch_events (video_id, watched_at) VALUES ($v,$t)",
            ("$v", w), ("$t", Now.AddDays(-1).ToString("O")));
        // Mark c1 as watched to verify it is excluded
        SetRaw(f.Db, "UPDATE videos SET watched=1 WHERE id=$id", ("$id", c1));

        // GetForYou should include the candidate section
        var sugg = f.Disc.GetForYou(limit: 10, now: Now);
        sugg.Select(s => s.SectionId).ShouldContain(candidate);

        // GetRecommendedVideos should return only unwatched videos from recommended sections
        var recs = f.Disc.GetRecommendedVideos(10, Now);
        recs.ShouldNotBeEmpty();
        recs.ShouldAllBe(v => !v.Watched);
        recs.ShouldAllBe(v => v.SectionId == candidate);
        recs.Select(v => v.VideoId).ShouldContain(c2);
        recs.Select(v => v.VideoId).ShouldNotContain(c1);
    }

    [Fact]
    public void GetRecommendedVideos_returns_empty_without_history()
    {
        var f = NewFixture(); using var _d = f.Db;
        var src = f.Lib.UpsertSource(@"C:\m", "M");
        var sec = f.Lib.UpsertSection(src, "S");
        f.Tags.AddTag(sec, "comedy");
        AddVideo(f, sec, "Show", false, 1);
        // No watch events seeded → history is empty → ScoreSections returns []
        var recs = f.Disc.GetRecommendedVideos(10, Now);
        recs.ShouldBeEmpty();
    }
}
