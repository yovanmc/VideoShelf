using Shouldly;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;
using Xunit;

namespace VideoShelf.Core.Tests.Storage;

public sealed class TagRepositoryTests
{
    private static (TempDb db, LibraryRepository lib, TagRepository tags, long sectionId) Seed()
    {
        var db = new TempDb();
        var lib = new LibraryRepository(db.Db);
        var sourceId = lib.UpsertSource(@"C:\media", "Media");
        var sectionId = lib.UpsertSection(sourceId, "Creator A");
        var tags = new TagRepository(db.Db);
        return (db, lib, tags, sectionId);
    }

    [Fact]
    public void AddTag_then_GetTags_returns_it()
    {
        var (db, _, tags, sectionId) = Seed();
        using var _d = db;
        tags.AddTag(sectionId, "Comedy");
        tags.GetTags(sectionId).ShouldBe(new[] { "comedy" });
    }

    [Fact]
    public void AddTag_normalizes_and_dedupes()
    {
        var (db, _, tags, sectionId) = Seed();
        using var _d = db;
        tags.AddTag(sectionId, "  Sci   Fi  ");
        tags.AddTag(sectionId, "sci fi");      // duplicate after normalization
        tags.AddTag(sectionId, "   ");          // ignored
        tags.GetTags(sectionId).ShouldBe(new[] { "sci fi" });
    }

    [Fact]
    public void RemoveTag_removes_only_that_tag()
    {
        var (db, _, tags, sectionId) = Seed();
        using var _d = db;
        tags.AddTag(sectionId, "comedy");
        tags.AddTag(sectionId, "drama");
        tags.RemoveTag(sectionId, "Comedy"); // case-insensitive
        tags.GetTags(sectionId).ShouldBe(new[] { "drama" });
    }

    [Fact]
    public void SetTags_replaces_all_and_orders_alphabetically()
    {
        var (db, _, tags, sectionId) = Seed();
        using var _d = db;
        tags.AddTag(sectionId, "zeta");
        tags.SetTags(sectionId, new[] { "Beta", "alpha", "beta" });
        tags.GetTags(sectionId).ShouldBe(new[] { "alpha", "beta" });
    }

    [Fact]
    public void GetAllTags_returns_distinct_sorted_across_sections()
    {
        var (db, lib, tags, sectionId) = Seed();
        using var _d = db;
        var section2 = lib.UpsertSection(lib.GetSources()[0].Id, "Creator B");
        tags.AddTag(sectionId, "comedy");
        tags.AddTag(section2, "comedy");
        tags.AddTag(section2, "action");
        tags.GetAllTags().ShouldBe(new[] { "action", "comedy" });
    }

    [Fact]
    public void GetTagCounts_counts_sections_per_tag()
    {
        var (db, lib, tags, sectionId) = Seed();
        using var _d = db;
        var section2 = lib.UpsertSection(lib.GetSources()[0].Id, "Creator B");
        tags.AddTag(sectionId, "comedy");
        tags.AddTag(section2, "comedy");
        tags.AddTag(section2, "action");
        var counts = tags.GetTagCounts();
        counts.ShouldContain(new TagCount("comedy", 2));
        counts.ShouldContain(new TagCount("action", 1));
    }

    // ── series-level ─────────────────────────────────────────────────────────

    private static (TempDb db, LibraryRepository lib, TagRepository tags, long sectionId, long seriesId) SeedSeries()
    {
        var db = new TempDb();
        var lib = new LibraryRepository(db.Db);
        var sourceId = lib.UpsertSource(@"C:\media", "Media");
        var sectionId = lib.UpsertSection(sourceId, "Creator A");
        var seriesId = lib.UpsertSeries(sectionId, "Show A", isStandalone: false);
        var tags = new TagRepository(db.Db);
        return (db, lib, tags, sectionId, seriesId);
    }

    [Fact]
    public void AddSeriesTag_then_GetSeriesTags_returns_it()
    {
        var (db, _, tags, _, seriesId) = SeedSeries();
        using var _d = db;
        tags.AddSeriesTag(seriesId, "Drama");
        tags.GetSeriesTags(seriesId).ShouldBe(new[] { "drama" });
    }

    [Fact]
    public void AddSeriesTag_normalizes_whitespace_and_dedupes()
    {
        var (db, _, tags, _, seriesId) = SeedSeries();
        using var _d = db;
        tags.AddSeriesTag(seriesId, "  Sci   Fi  ");
        tags.AddSeriesTag(seriesId, "sci fi");
        tags.AddSeriesTag(seriesId, "   ");
        tags.GetSeriesTags(seriesId).ShouldBe(new[] { "sci fi" });
    }

    [Fact]
    public void RemoveSeriesTag_removes_only_that_tag()
    {
        var (db, _, tags, _, seriesId) = SeedSeries();
        using var _d = db;
        tags.AddSeriesTag(seriesId, "comedy");
        tags.AddSeriesTag(seriesId, "drama");
        tags.RemoveSeriesTag(seriesId, "Comedy");
        tags.GetSeriesTags(seriesId).ShouldBe(new[] { "drama" });
    }

    [Fact]
    public void SetSeriesTags_replaces_all_and_orders_alphabetically()
    {
        var (db, _, tags, _, seriesId) = SeedSeries();
        using var _d = db;
        tags.AddSeriesTag(seriesId, "zeta");
        tags.SetSeriesTags(seriesId, new[] { "Beta", "alpha", "beta" });
        tags.GetSeriesTags(seriesId).ShouldBe(new[] { "alpha", "beta" });
    }

    // ── video-level ──────────────────────────────────────────────────────────

    private static (TempDb db, LibraryRepository lib, TagRepository tags, long sectionId, long seriesId, long videoId) SeedVideo()
    {
        var db = new TempDb();
        var lib = new LibraryRepository(db.Db);
        var sourceId = lib.UpsertSource(@"C:\media", "Media");
        var sectionId = lib.UpsertSection(sourceId, "Creator A");
        var seriesId = lib.UpsertSeries(sectionId, "Show A", isStandalone: false);
        var videoId = lib.UpsertVideo(seriesId, @"C:\media\Creator A\Show A\ep01.mkv", 1, "mkv");
        var tags = new TagRepository(db.Db);
        return (db, lib, tags, sectionId, seriesId, videoId);
    }

    [Fact]
    public void AddVideoTag_then_GetVideoTags_returns_it()
    {
        var (db, _, tags, _, _, videoId) = SeedVideo();
        using var _d = db;
        tags.AddVideoTag(videoId, "Action");
        tags.GetVideoTags(videoId).ShouldBe(new[] { "action" });
    }

    [Fact]
    public void AddVideoTag_normalizes_whitespace_and_dedupes()
    {
        var (db, _, tags, _, _, videoId) = SeedVideo();
        using var _d = db;
        tags.AddVideoTag(videoId, "  Sci   Fi  ");
        tags.AddVideoTag(videoId, "sci fi");
        tags.AddVideoTag(videoId, "   ");
        tags.GetVideoTags(videoId).ShouldBe(new[] { "sci fi" });
    }

    [Fact]
    public void RemoveVideoTag_removes_only_that_tag()
    {
        var (db, _, tags, _, _, videoId) = SeedVideo();
        using var _d = db;
        tags.AddVideoTag(videoId, "comedy");
        tags.AddVideoTag(videoId, "drama");
        tags.RemoveVideoTag(videoId, "Comedy");
        tags.GetVideoTags(videoId).ShouldBe(new[] { "drama" });
    }

    [Fact]
    public void SetVideoTags_replaces_all_and_orders_alphabetically()
    {
        var (db, _, tags, _, _, videoId) = SeedVideo();
        using var _d = db;
        tags.AddVideoTag(videoId, "zeta");
        tags.SetVideoTags(videoId, new[] { "Beta", "alpha", "beta" });
        tags.GetVideoTags(videoId).ShouldBe(new[] { "alpha", "beta" });
    }

    // ── GetEffectiveVideoTags ─────────────────────────────────────────────────

    [Fact]
    public void GetEffectiveVideoTags_unions_section_series_video_deduped_sorted()
    {
        var (db, _, tags, sectionId, seriesId, videoId) = SeedVideo();
        using var _d = db;
        // one tag at each level; "shared" is at all three → must appear once
        tags.AddTag(sectionId, "section-only");
        tags.AddTag(sectionId, "shared");
        tags.AddSeriesTag(seriesId, "series-only");
        tags.AddSeriesTag(seriesId, "shared");
        tags.AddVideoTag(videoId, "video-only");
        tags.AddVideoTag(videoId, "shared");

        var effective = tags.GetEffectiveVideoTags(videoId);
        effective.ShouldBe(new[] { "section-only", "series-only", "shared", "video-only" });
    }

    [Fact]
    public void GetEffectiveVideoTags_returns_empty_when_no_tags_set()
    {
        var (db, _, tags, _, _, videoId) = SeedVideo();
        using var _d = db;
        tags.GetEffectiveVideoTags(videoId).ShouldBeEmpty();
    }

    [Fact]
    public void GetEffectiveVideoTags_does_not_leak_tags_from_other_series_or_section()
    {
        // Arrange: one source, one section, two series; target video is in series A only.
        var db = new TempDb();
        using var _d = db;
        var lib = new LibraryRepository(db.Db);
        var tags = new TagRepository(db.Db);

        var sourceId = lib.UpsertSource(@"C:\media", "Media");
        var sectionId = lib.UpsertSection(sourceId, "Creator A");

        // Series A — the one our target video belongs to.
        var seriesAId = lib.UpsertSeries(sectionId, "Show A", isStandalone: false);
        var targetVideoId = lib.UpsertVideo(seriesAId, @"C:\media\Creator A\Show A\ep01.mkv", 1, "mkv");

        // Series B — a sibling series in the SAME section; must not bleed into target.
        var seriesBId = lib.UpsertSeries(sectionId, "Show B", isStandalone: false);
        // (no video in series B needed — tags are on the series row itself)

        // Second section in the same source — its tags must not bleed in either.
        var section2Id = lib.UpsertSection(sourceId, "Creator B");

        // Legitimate tags: section-level on sectionId, series-level on seriesAId.
        tags.AddTag(sectionId, "section-tag");
        tags.AddSeriesTag(seriesAId, "series-a-tag");

        // Noise tags that must NOT appear in the result.
        tags.AddSeriesTag(seriesBId, "other-series-tag");
        tags.AddTag(section2Id, "other-section-tag");

        // Act
        var effective = tags.GetEffectiveVideoTags(targetVideoId);

        // Assert: legitimate tags present, noise tags absent.
        effective.ShouldContain("section-tag");
        effective.ShouldContain("series-a-tag");
        effective.ShouldNotContain("other-series-tag");
        effective.ShouldNotContain("other-section-tag");
    }

    // ── GetAllTagsAcrossLevels ────────────────────────────────────────────────

    [Fact]
    public void GetAllTagsAcrossLevels_returns_distinct_union_sorted()
    {
        var (db, _, tags, sectionId, seriesId, videoId) = SeedVideo();
        using var _d = db;
        tags.AddTag(sectionId, "alpha");
        tags.AddTag(sectionId, "shared");
        tags.AddSeriesTag(seriesId, "beta");
        tags.AddSeriesTag(seriesId, "shared");
        tags.AddVideoTag(videoId, "gamma");
        tags.AddVideoTag(videoId, "shared");

        tags.GetAllTagsAcrossLevels().ShouldBe(new[] { "alpha", "beta", "gamma", "shared" });
    }

    [Fact]
    public void GetAllTagsAcrossLevels_returns_empty_when_no_tags()
    {
        var (db, _, tags, _, _, _) = SeedVideo();
        using var _d = db;
        tags.GetAllTagsAcrossLevels().ShouldBeEmpty();
    }
}
