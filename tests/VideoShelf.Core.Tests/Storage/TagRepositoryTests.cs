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
}
