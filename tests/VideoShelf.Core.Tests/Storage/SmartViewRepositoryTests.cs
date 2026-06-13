using System;
using System.Linq;
using Shouldly;
using VideoShelf.Core.Discovery;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;
using Xunit;

namespace VideoShelf.Core.Tests.Storage;

public sealed class SmartViewRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 13, 12, 0, 0, TimeSpan.Zero);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static SmartViewDefinition EmptyDef() =>
        new SmartViewDefinition("all", Array.Empty<SmartRule>());

    private static SmartViewDefinition WatchedFalseDef() =>
        new SmartViewDefinition("all", new[] { new SmartRule("watched", "is", "false") });

    private static void SetRaw(TempDb db, string sql, params (string Name, object Value)[] ps)
    {
        using var conn = db.Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in ps) cmd.Parameters.AddWithValue(k, v);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Seeds a minimal section + series + video. Returns (sectionId, seriesId, videoId).</summary>
    private static (long SectionId, long SeriesId, long VideoId) SeedVideo(
        TempDb db, LibraryRepository lib, string sourcePath = @"C:\m", string sectionName = "Creator",
        string seriesTitle = "Show", int ep = 1)
    {
        var sourceId = lib.UpsertSource(sourcePath, sourcePath);
        var sectionId = lib.UpsertSection(sourceId, sectionName);
        var seriesId = lib.UpsertSeries(sectionId, seriesTitle, isStandalone: false);
        var videoId = lib.UpsertVideo(seriesId, $@"{sourcePath}\{sectionName}\{seriesTitle}\e{ep:00}.mkv", ep, "mkv");
        return (sectionId, seriesId, videoId);
    }

    // ── CRUD ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Create_roundtrips_all_fields_and_GetAll_returns_it()
    {
        using var db = new TempDb();
        var repo = new SmartViewRepository(db.Db);
        var def = new SmartViewDefinition("all", new[] { new SmartRule("watched", "is", "false") });

        var id = repo.Create("Unwatched", def, showOnHome: true, now: Now);

        id.ShouldBeGreaterThan(0);
        var all = repo.GetAll();
        all.Count.ShouldBe(1);
        var sv = all[0];
        sv.Id.ShouldBe(id);
        sv.Name.ShouldBe("Unwatched");
        sv.ShowOnHome.ShouldBeTrue();
        sv.CreatedAt.ShouldBe(Now.ToString("o"));
        sv.Definition.Match.ShouldBe("all");
        sv.Definition.Rules.Count.ShouldBe(1);
        sv.Definition.Rules[0].Field.ShouldBe("watched");
        sv.Definition.Rules[0].Op.ShouldBe("is");
        sv.Definition.Rules[0].Value.ShouldBe("false");
    }

    [Fact]
    public void Update_changes_name_definition_and_showOnHome()
    {
        using var db = new TempDb();
        var repo = new SmartViewRepository(db.Db);
        var id = repo.Create("Old Name", EmptyDef(), showOnHome: true, now: Now);

        var newDef = new SmartViewDefinition("any", new[] { new SmartRule("duration", "gt", "3600") });
        repo.Update(id, "New Name", newDef, showOnHome: false);

        var sv = repo.GetAll().Single();
        sv.Name.ShouldBe("New Name");
        sv.ShowOnHome.ShouldBeFalse();
        sv.Definition.Match.ShouldBe("any");
        sv.Definition.Rules[0].Field.ShouldBe("duration");
    }

    [Fact]
    public void Delete_removes_row()
    {
        using var db = new TempDb();
        var repo = new SmartViewRepository(db.Db);
        var id = repo.Create("ToDelete", EmptyDef(), showOnHome: true, now: Now);

        repo.Delete(id);

        repo.GetAll().ShouldBeEmpty();
    }

    [Fact]
    public void Reorder_updates_sort_order_and_GetAll_respects_it()
    {
        using var db = new TempDb();
        var repo = new SmartViewRepository(db.Db);
        var id1 = repo.Create("First", EmptyDef(), showOnHome: true, now: Now);
        var id2 = repo.Create("Second", EmptyDef(), showOnHome: true, now: Now);

        // By default both have sort_order=0; flip them by setting id2 to -1
        repo.Reorder(id2, -1);

        var all = repo.GetAll();
        all[0].Id.ShouldBe(id2); // -1 sorts before 0
        all[1].Id.ShouldBe(id1);
    }

    [Fact]
    public void GetHomeViews_excludes_showOnHome_false()
    {
        using var db = new TempDb();
        var repo = new SmartViewRepository(db.Db);
        var homeId = repo.Create("Home", EmptyDef(), showOnHome: true, now: Now);
        repo.Create("Hidden", EmptyDef(), showOnHome: false, now: Now);

        var home = repo.GetHomeViews();

        home.Count.ShouldBe(1);
        home[0].Id.ShouldBe(homeId);
    }

    // ── GetMatchingVideos ─────────────────────────────────────────────────────

    private static (TempDb Db, LibraryRepository Lib, TagRepository Tags) NewFixture()
    {
        var db = new TempDb();
        var lib = new LibraryRepository(db.Db);
        var tags = new TagRepository(db.Db);
        return (db, lib, tags);
    }

    [Fact]
    public void GetMatchingVideos_emptyRules_returns_all_nonmissing()
    {
        var (db, lib, _) = NewFixture();
        using var _d = db;
        var repo = new SmartViewRepository(db.Db);
        var (_, _, vid1) = SeedVideo(db, lib, sectionName: "S1", seriesTitle: "Show1", ep: 1);
        var (_, _, vid2) = SeedVideo(db, lib, sectionName: "S2", seriesTitle: "Show2", ep: 1);
        // Mark vid2 as missing — should be excluded
        SetRaw(db, "UPDATE videos SET missing=1 WHERE id=$id", ("$id", vid2));

        var results = repo.GetMatchingVideos(EmptyDef(), limit: 100, now: Now);

        results.Select(r => r.VideoId).ShouldContain(vid1);
        results.Select(r => r.VideoId).ShouldNotContain(vid2);
    }

    [Fact]
    public void GetMatchingVideos_missing1_never_returned()
    {
        var (db, lib, _) = NewFixture();
        using var _d = db;
        var repo = new SmartViewRepository(db.Db);
        var (_, _, vid) = SeedVideo(db, lib);
        SetRaw(db, "UPDATE videos SET missing=1 WHERE id=$id", ("$id", vid));

        var results = repo.GetMatchingVideos(EmptyDef(), limit: 100, now: Now);

        results.ShouldBeEmpty();
    }

    [Fact]
    public void GetMatchingVideos_watched_is_false_returns_only_unwatched()
    {
        var (db, lib, _) = NewFixture();
        using var _d = db;
        var repo = new SmartViewRepository(db.Db);
        var srcId = lib.UpsertSource(@"C:\m", "M");
        var secId = lib.UpsertSection(srcId, "S");
        var seriesId = lib.UpsertSeries(secId, "Show", isStandalone: false);
        var unwatched = lib.UpsertVideo(seriesId, @"C:\m\S\Show\e01.mkv", 1, "mkv");
        var watched = lib.UpsertVideo(seriesId, @"C:\m\S\Show\e02.mkv", 2, "mkv");
        SetRaw(db, "UPDATE videos SET watched=1 WHERE id=$id", ("$id", watched));

        var def = new SmartViewDefinition("all", new[] { new SmartRule("watched", "is", "false") });
        var results = repo.GetMatchingVideos(def, limit: 100, now: Now);

        results.Select(r => r.VideoId).ShouldContain(unwatched);
        results.Select(r => r.VideoId).ShouldNotContain(watched);
        results.ShouldAllBe(r => !r.Watched);
    }

    [Fact]
    public void GetMatchingVideos_creator_is_sectionId_filters_by_section()
    {
        var (db, lib, _) = NewFixture();
        using var _d = db;
        var repo = new SmartViewRepository(db.Db);
        var srcId = lib.UpsertSource(@"C:\m", "M");
        var sec1 = lib.UpsertSection(srcId, "Creator1");
        var sec2 = lib.UpsertSection(srcId, "Creator2");
        var ser1 = lib.UpsertSeries(sec1, "Show1", isStandalone: false);
        var ser2 = lib.UpsertSeries(sec2, "Show2", isStandalone: false);
        var vid1 = lib.UpsertVideo(ser1, @"C:\m\Creator1\Show1\e01.mkv", 1, "mkv");
        var vid2 = lib.UpsertVideo(ser2, @"C:\m\Creator2\Show2\e01.mkv", 1, "mkv");

        var def = new SmartViewDefinition("all", new[] { new SmartRule("creator", "is", sec1.ToString()) });
        var results = repo.GetMatchingVideos(def, limit: 100, now: Now);

        results.Select(r => r.VideoId).ShouldContain(vid1);
        results.Select(r => r.VideoId).ShouldNotContain(vid2);
        results.ShouldAllBe(r => r.SectionId == sec1);
    }

    [Fact]
    public void GetMatchingVideos_tag_is_matches_video_and_series_level_tags()
    {
        var (db, lib, tags) = NewFixture();
        using var _d = db;
        var repo = new SmartViewRepository(db.Db);
        var srcId = lib.UpsertSource(@"C:\m", "M");
        var secId = lib.UpsertSection(srcId, "S");
        var ser1 = lib.UpsertSeries(secId, "Show1", isStandalone: false);
        var ser2 = lib.UpsertSeries(secId, "Show2", isStandalone: false);
        var ser3 = lib.UpsertSeries(secId, "Show3", isStandalone: false);
        var vid1 = lib.UpsertVideo(ser1, @"C:\m\S\Show1\e01.mkv", 1, "mkv"); // tag on video
        var vid2 = lib.UpsertVideo(ser2, @"C:\m\S\Show2\e01.mkv", 1, "mkv"); // tag on series
        var vid3 = lib.UpsertVideo(ser3, @"C:\m\S\Show3\e01.mkv", 1, "mkv"); // no tag

        tags.AddVideoTag(vid1, "comedy");
        tags.AddSeriesTag(ser2, "comedy");
        // vid3 / ser3 intentionally left without "comedy"

        var def = new SmartViewDefinition("all", new[] { new SmartRule("tag", "is", "comedy") });
        var results = repo.GetMatchingVideos(def, limit: 100, now: Now);

        results.Select(r => r.VideoId).ShouldContain(vid1);
        results.Select(r => r.VideoId).ShouldContain(vid2);
        results.Select(r => r.VideoId).ShouldNotContain(vid3);
    }

    [Fact]
    public void GetMatchingVideos_dateAdded_withinDays_filters_recent_vs_old()
    {
        var (db, lib, _) = NewFixture();
        using var _d = db;
        var repo = new SmartViewRepository(db.Db);
        var srcId = lib.UpsertSource(@"C:\m", "M");
        var secId = lib.UpsertSection(srcId, "S");
        var seriesId = lib.UpsertSeries(secId, "Show", isStandalone: false);
        var recentVid = lib.UpsertVideo(seriesId, @"C:\m\S\Show\e01.mkv", 1, "mkv");
        var oldVid = lib.UpsertVideo(seriesId, @"C:\m\S\Show\e02.mkv", 2, "mkv");

        // Set added_at: recent = 5 days ago, old = 30 days ago
        var recentAt = Now.AddDays(-5).ToString("o");
        var oldAt = Now.AddDays(-30).ToString("o");
        SetRaw(db, "UPDATE videos SET added_at=$at WHERE id=$id", ("$at", recentAt), ("$id", recentVid));
        SetRaw(db, "UPDATE videos SET added_at=$at WHERE id=$id", ("$at", oldAt), ("$id", oldVid));

        var def = new SmartViewDefinition("all", new[] { new SmartRule("dateAdded", "withinDays", "7") });
        var results = repo.GetMatchingVideos(def, limit: 100, now: Now);

        results.Select(r => r.VideoId).ShouldContain(recentVid);
        results.Select(r => r.VideoId).ShouldNotContain(oldVid);
    }

    [Fact]
    public void GetMatchingVideos_duration_gt_excludes_null_and_shorter_videos()
    {
        var (db, lib, _) = NewFixture();
        using var _d = db;
        var repo = new SmartViewRepository(db.Db);
        var srcId = lib.UpsertSource(@"C:\m", "M");
        var secId = lib.UpsertSection(srcId, "S");
        var seriesId = lib.UpsertSeries(secId, "Show", isStandalone: false);
        var longVid = lib.UpsertVideo(seriesId, @"C:\m\S\Show\e01.mkv", 1, "mkv");
        var shortVid = lib.UpsertVideo(seriesId, @"C:\m\S\Show\e02.mkv", 2, "mkv");
        var nullVid = lib.UpsertVideo(seriesId, @"C:\m\S\Show\e03.mkv", 3, "mkv");

        lib.SetDuration(longVid, 7200.0);  // 2 hours
        lib.SetDuration(shortVid, 600.0);  // 10 minutes
        // nullVid intentionally left with NULL duration

        var def = new SmartViewDefinition("all", new[] { new SmartRule("duration", "gt", "3600") });
        var results = repo.GetMatchingVideos(def, limit: 100, now: Now);

        results.Select(r => r.VideoId).ShouldContain(longVid);
        results.Select(r => r.VideoId).ShouldNotContain(shortVid);
        results.Select(r => r.VideoId).ShouldNotContain(nullVid);
    }

    [Fact]
    public void GetMatchingVideos_match_all_requires_all_rules()
    {
        var (db, lib, _) = NewFixture();
        using var _d = db;
        var repo = new SmartViewRepository(db.Db);
        var srcId = lib.UpsertSource(@"C:\m", "M");
        var secId = lib.UpsertSection(srcId, "S");
        var seriesId = lib.UpsertSeries(secId, "Show", isStandalone: false);
        var bothMatch = lib.UpsertVideo(seriesId, @"C:\m\S\Show\e01.mkv", 1, "mkv");
        var onlyWatched = lib.UpsertVideo(seriesId, @"C:\m\S\Show\e02.mkv", 2, "mkv");
        var neither = lib.UpsertVideo(seriesId, @"C:\m\S\Show\e03.mkv", 3, "mkv");

        lib.SetDuration(bothMatch, 7200.0);
        SetRaw(db, "UPDATE videos SET watched=0 WHERE id=$id", ("$id", bothMatch));
        lib.SetDuration(onlyWatched, 7200.0);
        SetRaw(db, "UPDATE videos SET watched=1 WHERE id=$id", ("$id", onlyWatched));
        // neither: no duration, watched=0

        // match=all: duration > 3600 AND watched = false
        var def = new SmartViewDefinition("all", new[]
        {
            new SmartRule("duration", "gt", "3600"),
            new SmartRule("watched", "is", "false")
        });
        var results = repo.GetMatchingVideos(def, limit: 100, now: Now);

        results.Select(r => r.VideoId).ShouldContain(bothMatch);
        results.Select(r => r.VideoId).ShouldNotContain(onlyWatched);
        results.Select(r => r.VideoId).ShouldNotContain(neither);
    }

    [Fact]
    public void GetMatchingVideos_match_any_requires_at_least_one_rule()
    {
        var (db, lib, _) = NewFixture();
        using var _d = db;
        var repo = new SmartViewRepository(db.Db);
        var srcId = lib.UpsertSource(@"C:\m", "M");
        var secId = lib.UpsertSection(srcId, "S");
        var seriesId = lib.UpsertSeries(secId, "Show", isStandalone: false);
        var longVid = lib.UpsertVideo(seriesId, @"C:\m\S\Show\e01.mkv", 1, "mkv");
        var watchedVid = lib.UpsertVideo(seriesId, @"C:\m\S\Show\e02.mkv", 2, "mkv");
        var neitherVid = lib.UpsertVideo(seriesId, @"C:\m\S\Show\e03.mkv", 3, "mkv");

        lib.SetDuration(longVid, 7200.0);
        SetRaw(db, "UPDATE videos SET watched=0 WHERE id=$id", ("$id", longVid));
        // watchedVid: short, but watched
        SetRaw(db, "UPDATE videos SET watched=1 WHERE id=$id", ("$id", watchedVid));
        lib.SetDuration(watchedVid, 100.0);
        // neitherVid: no duration, not watched

        // match=any: duration > 3600 OR watched = true
        var def = new SmartViewDefinition("any", new[]
        {
            new SmartRule("duration", "gt", "3600"),
            new SmartRule("watched", "is", "true")
        });
        var results = repo.GetMatchingVideos(def, limit: 100, now: Now);

        results.Select(r => r.VideoId).ShouldContain(longVid);
        results.Select(r => r.VideoId).ShouldContain(watchedVid);
        results.Select(r => r.VideoId).ShouldNotContain(neitherVid);
    }
}
