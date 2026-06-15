using Shouldly;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;
using Xunit;

namespace VideoShelf.Core.Tests.Storage;

public sealed class LibrarySearchTests
{
    private sealed record Fixture(TempDb Db, LibraryRepository Lib);

    private static Fixture NewFixture()
    {
        var db = new TempDb();
        var lib = new LibraryRepository(db.Db);
        return new Fixture(db, lib);
    }

    private static long AddVideo(Fixture f, long sectionId, string series, bool standalone, int ep,
        string? fileNameOverride = null)
    {
        var ser = f.Lib.UpsertSeries(sectionId, series, standalone);
        var fileName = fileNameOverride ?? $"e{ep:00}.mkv";
        return f.Lib.UpsertVideo(ser, $@"C:\m\{series}\{fileName}", ep, "mkv");
    }

    // ─── SearchCreators ────────────────────────────────────────────────────────

    [Fact]
    public void SearchCreators_matches_by_display_name_substring()
    {
        var f = NewFixture(); using var _d = f.Db;
        var src = f.Lib.UpsertSource(@"C:\m", "M");
        var secA = f.Lib.UpsertSection(src, "Action Movies");
        var secB = f.Lib.UpsertSection(src, "Comedy Shows");
        AddVideo(f, secA, "Die Hard", false, 1);
        AddVideo(f, secB, "Seinfeld", false, 1);

        var results = f.Lib.SearchCreators("action", limit: 10);

        results.Count.ShouldBe(1);
        results[0].SectionId.ShouldBe(secA);
        results[0].DisplayName.ShouldBe("Action Movies");
    }

    [Fact]
    public void SearchCreators_is_case_insensitive()
    {
        var f = NewFixture(); using var _d = f.Db;
        var src = f.Lib.UpsertSource(@"C:\m", "M");
        var sec = f.Lib.UpsertSection(src, "Drama Series");
        AddVideo(f, sec, "Show1", false, 1);

        // SQLite LIKE is case-insensitive for ASCII characters
        var lower = f.Lib.SearchCreators("drama", limit: 10);
        var upper = f.Lib.SearchCreators("DRAMA", limit: 10);

        lower.Count.ShouldBe(1);
        upper.Count.ShouldBe(1);
        lower[0].SectionId.ShouldBe(sec);
        upper[0].SectionId.ShouldBe(sec);
    }

    [Fact]
    public void SearchCreators_returns_correct_video_count()
    {
        var f = NewFixture(); using var _d = f.Db;
        var src = f.Lib.UpsertSource(@"C:\m", "M");
        var sec = f.Lib.UpsertSection(src, "Sci-Fi");
        AddVideo(f, sec, "Star Wars", false, 1);
        AddVideo(f, sec, "Star Wars", false, 2);
        AddVideo(f, sec, "Star Wars", false, 3);

        var results = f.Lib.SearchCreators("sci", limit: 10);

        results.Count.ShouldBe(1);
        results[0].VideoCount.ShouldBe(3);
    }

    [Fact]
    public void SearchCreators_respects_limit()
    {
        var f = NewFixture(); using var _d = f.Db;
        var src = f.Lib.UpsertSource(@"C:\m", "M");
        f.Lib.UpsertSection(src, "Action Alpha");
        f.Lib.UpsertSection(src, "Action Beta");
        f.Lib.UpsertSection(src, "Action Gamma");

        var results = f.Lib.SearchCreators("action", limit: 2);

        results.Count.ShouldBe(2);
    }

    [Fact]
    public void SearchCreators_empty_query_returns_empty()
    {
        var f = NewFixture(); using var _d = f.Db;
        var src = f.Lib.UpsertSource(@"C:\m", "M");
        f.Lib.UpsertSection(src, "Action Movies");

        f.Lib.SearchCreators("", limit: 10).ShouldBeEmpty();
        f.Lib.SearchCreators("   ", limit: 10).ShouldBeEmpty();
    }

    // ─── SearchVideos ──────────────────────────────────────────────────────────

    [Fact]
    public void SearchVideos_matches_by_raw_filename()
    {
        var f = NewFixture(); using var _d = f.Db;
        var src = f.Lib.UpsertSource(@"C:\m", "M");
        var sec = f.Lib.UpsertSection(src, "Movies");
        AddVideo(f, sec, "Series A", false, 1, "the.godfather.mkv");
        AddVideo(f, sec, "Series B", false, 1, "star.wars.mkv");

        var results = f.Lib.SearchVideos("godfather", limit: 10);

        results.Count.ShouldBe(1);
        results[0].SeriesTitle.ShouldBe("Series A");
    }

    [Fact]
    public void SearchVideos_matches_by_series_base_title()
    {
        var f = NewFixture(); using var _d = f.Db;
        var src = f.Lib.UpsertSource(@"C:\m", "M");
        var sec = f.Lib.UpsertSection(src, "TV");
        var serA = f.Lib.UpsertSeries(sec, "Breaking Bad", false);
        var serB = f.Lib.UpsertSeries(sec, "Better Call Saul", false);
        f.Lib.UpsertVideo(serA, @"C:\m\Breaking Bad\e01.mkv", 1, "mkv");
        f.Lib.UpsertVideo(serA, @"C:\m\Breaking Bad\e02.mkv", 2, "mkv");
        f.Lib.UpsertVideo(serB, @"C:\m\Better Call Saul\e01.mkv", 1, "mkv");

        var results = f.Lib.SearchVideos("breaking bad", limit: 10);

        results.Count.ShouldBe(2);
        results.ShouldAllBe(v => v.SeriesTitle == "Breaking Bad");
    }

    [Fact]
    public void SearchVideos_excludes_missing_videos()
    {
        var f = NewFixture(); using var _d = f.Db;
        var src = f.Lib.UpsertSource(@"C:\m", "M");
        var sec = f.Lib.UpsertSection(src, "Movies");
        var vid = AddVideo(f, sec, "Interstellar", true, 1, "interstellar.mkv");

        // Mark as missing
        using var conn = f.Db.Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE videos SET missing = 1 WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", vid);
        cmd.ExecuteNonQuery();

        var results = f.Lib.SearchVideos("interstellar", limit: 10);

        results.ShouldBeEmpty();
    }

    [Fact]
    public void SearchVideos_respects_limit()
    {
        var f = NewFixture(); using var _d = f.Db;
        var src = f.Lib.UpsertSource(@"C:\m", "M");
        var sec = f.Lib.UpsertSection(src, "TV");
        var ser = f.Lib.UpsertSeries(sec, "Friends", false);
        for (int i = 1; i <= 5; i++)
            f.Lib.UpsertVideo(ser, $@"C:\m\Friends\e{i:00}.mkv", i, "mkv");

        var results = f.Lib.SearchVideos("friends", limit: 3);

        results.Count.ShouldBe(3);
    }

    [Fact]
    public void SearchVideos_empty_query_returns_empty()
    {
        var f = NewFixture(); using var _d = f.Db;
        var src = f.Lib.UpsertSource(@"C:\m", "M");
        var sec = f.Lib.UpsertSection(src, "Movies");
        AddVideo(f, sec, "Inception", true, 1);

        f.Lib.SearchVideos("", limit: 10).ShouldBeEmpty();
        f.Lib.SearchVideos("   ", limit: 10).ShouldBeEmpty();
    }

    [Fact]
    public void SearchVideos_returns_correct_recency_item_fields()
    {
        var f = NewFixture(); using var _d = f.Db;
        var src = f.Lib.UpsertSource(@"C:\m", "M");
        var sec = f.Lib.UpsertSection(src, "Movies");
        var ser = f.Lib.UpsertSeries(sec, "Inception", true);
        f.Lib.UpsertVideo(ser, @"C:\m\Inception\inception.mkv", 1, "mkv");

        var results = f.Lib.SearchVideos("inception", limit: 10);

        results.Count.ShouldBe(1);
        var item = results[0];
        item.SeriesTitle.ShouldBe("Inception");
        item.IsStandalone.ShouldBeTrue();
        item.EpisodeNo.ShouldBe(1);
        item.SectionId.ShouldBe(sec);
    }

    // ─── SearchSeries ──────────────────────────────────────────────────────────

    [Fact]
    public void SearchSeries_matches_by_base_title_substring()
    {
        var f = NewFixture(); using var _d = f.Db;
        var src = f.Lib.UpsertSource(@"C:\m", "M");
        var sec = f.Lib.UpsertSection(src, "TV");
        AddVideo(f, sec, "Breaking Bad", false, 1);
        AddVideo(f, sec, "Better Call Saul", false, 1);

        var results = f.Lib.SearchSeries("breaking", limit: 10);

        results.Count.ShouldBe(1);
        results[0].Title.ShouldBe("Breaking Bad");
    }

    [Fact]
    public void SearchSeries_returns_episode_count_for_non_missing_videos()
    {
        var f = NewFixture(); using var _d = f.Db;
        var src = f.Lib.UpsertSource(@"C:\m", "M");
        var sec = f.Lib.UpsertSection(src, "TV");
        var seriesId = f.Lib.UpsertSeries(sec, "The Office", false);
        f.Lib.UpsertVideo(seriesId, @"C:\m\The Office\e01.mkv", 1, "mkv");
        f.Lib.UpsertVideo(seriesId, @"C:\m\The Office\e02.mkv", 2, "mkv");
        f.Lib.UpsertVideo(seriesId, @"C:\m\The Office\e03.mkv", 3, "mkv");

        var results = f.Lib.SearchSeries("office", limit: 10);

        results.Count.ShouldBe(1);
        results[0].EpisodeCount.ShouldBe(3);
    }

    [Fact]
    public void SearchSeries_excludes_missing_only_series()
    {
        var f = NewFixture(); using var _d = f.Db;
        var src = f.Lib.UpsertSource(@"C:\m", "M");
        var sec = f.Lib.UpsertSection(src, "TV");
        var seriesId = f.Lib.UpsertSeries(sec, "Lost", false);
        var videoId = f.Lib.UpsertVideo(seriesId, @"C:\m\Lost\e01.mkv", 1, "mkv");

        // Mark the only video as missing — series should be excluded
        f.Lib.SetVideoMissing(videoId);

        var results = f.Lib.SearchSeries("lost", limit: 10);

        results.ShouldBeEmpty();
    }

    [Fact]
    public void SearchSeries_includes_section_id()
    {
        var f = NewFixture(); using var _d = f.Db;
        var src = f.Lib.UpsertSource(@"C:\m", "M");
        var sec = f.Lib.UpsertSection(src, "Documentary");
        AddVideo(f, sec, "Planet Earth", false, 1);

        var results = f.Lib.SearchSeries("planet", limit: 10);

        results.Count.ShouldBe(1);
        results[0].SectionId.ShouldBe(sec);
    }

    [Fact]
    public void SearchSeries_respects_limit()
    {
        var f = NewFixture(); using var _d = f.Db;
        var src = f.Lib.UpsertSource(@"C:\m", "M");
        var sec = f.Lib.UpsertSection(src, "TV");
        AddVideo(f, sec, "Star Trek TOS", false, 1);
        AddVideo(f, sec, "Star Trek TNG", false, 1);
        AddVideo(f, sec, "Star Trek DS9", false, 1);

        var results = f.Lib.SearchSeries("star trek", limit: 2);

        results.Count.ShouldBe(2);
    }

    [Fact]
    public void SearchSeries_empty_query_returns_empty()
    {
        var f = NewFixture(); using var _d = f.Db;
        var src = f.Lib.UpsertSource(@"C:\m", "M");
        var sec = f.Lib.UpsertSection(src, "TV");
        AddVideo(f, sec, "Seinfeld", false, 1);

        f.Lib.SearchSeries("", limit: 10).ShouldBeEmpty();
        f.Lib.SearchSeries("   ", limit: 10).ShouldBeEmpty();
    }

    [Fact]
    public void SearchSeries_thumbnail_seed_path_is_first_non_missing_episode()
    {
        var f = NewFixture(); using var _d = f.Db;
        var src = f.Lib.UpsertSource(@"C:\m", "M");
        var sec = f.Lib.UpsertSection(src, "TV");
        var seriesId = f.Lib.UpsertSeries(sec, "Friends", false);
        f.Lib.UpsertVideo(seriesId, @"C:\m\Friends\e01.mkv", 1, "mkv");
        f.Lib.UpsertVideo(seriesId, @"C:\m\Friends\e02.mkv", 2, "mkv");

        var results = f.Lib.SearchSeries("friends", limit: 10);

        results.Count.ShouldBe(1);
        results[0].ThumbnailSeedPath.ShouldBe(@"C:\m\Friends\e01.mkv");
    }
}
