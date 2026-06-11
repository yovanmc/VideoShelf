using System.Linq;
using Shouldly;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;

namespace VideoShelf.Core.Tests.Storage;

public class AddedAtTests
{
    [Fact]
    public void UpsertVideo_stamps_added_at_on_first_insert()
    {
        using var temp = new TempDb();
        var repo = new LibraryRepository(temp.Db);
        var seriesId = repo.UpsertSeries(repo.UpsertSection(repo.UpsertSource(@"C:\V", "V"), "S"), "Base", false);

        repo.UpsertVideo(seriesId, @"C:\V\S\a.mp4", 1, ".mp4");

        var v = repo.GetVideosForSeries(seriesId).Single();
        v.AddedAt.ShouldNotBeNullOrEmpty();
        v.Missing.ShouldBeFalse();
    }

    [Fact]
    public void Rescan_preserves_original_added_at()
    {
        using var temp = new TempDb();
        var repo = new LibraryRepository(temp.Db);
        var seriesId = repo.UpsertSeries(repo.UpsertSection(repo.UpsertSource(@"C:\V", "V"), "S"), "Base", false);

        repo.UpsertVideo(seriesId, @"C:\V\S\a.mp4", 1, ".mp4");
        var first = repo.GetVideosForSeries(seriesId).Single().AddedAt;

        repo.UpsertVideo(seriesId, @"C:\V\S\a.mp4", 2, ".mp4"); // re-upsert
        var second = repo.GetVideosForSeries(seriesId).Single().AddedAt;

        second.ShouldBe(first); // added_at is not overwritten
    }
}
