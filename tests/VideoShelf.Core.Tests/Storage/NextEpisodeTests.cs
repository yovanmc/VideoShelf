using Shouldly;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;

namespace VideoShelf.Core.Tests.Storage;

public class NextEpisodeTests
{
    private static (LibraryRepository lib, long seriesId) SeedSeries(TempDb temp, bool standalone)
    {
        var lib = new LibraryRepository(temp.Db);
        var sectionId = lib.UpsertSection(lib.UpsertSource(@"C:\V", "V"), "S");
        var seriesId = lib.UpsertSeries(sectionId, "Base", standalone);
        return (lib, seriesId);
    }

    [Fact]
    public void GetNextEpisode_returns_next_by_episode_no()
    {
        using var temp = new TempDb();
        var (lib, seriesId) = SeedSeries(temp, standalone: false);
        lib.UpsertVideo(seriesId, @"C:\V\S\a.mp4", 1, ".mp4");
        lib.UpsertVideo(seriesId, @"C:\V\S\b.mp4", 2, ".mp4");

        var next = lib.GetNextEpisode(seriesId, 1);

        next.ShouldNotBeNull();
        next!.EpisodeNo.ShouldBe(2);
        next.FilePath.ShouldBe(@"C:\V\S\b.mp4");
    }

    [Fact]
    public void GetNextEpisode_returns_null_at_last_episode()
    {
        using var temp = new TempDb();
        var (lib, seriesId) = SeedSeries(temp, standalone: false);
        lib.UpsertVideo(seriesId, @"C:\V\S\a.mp4", 1, ".mp4");
        lib.UpsertVideo(seriesId, @"C:\V\S\b.mp4", 2, ".mp4");

        lib.GetNextEpisode(seriesId, 2).ShouldBeNull();
    }

    [Fact]
    public void GetNextEpisode_returns_null_for_standalone_series()
    {
        using var temp = new TempDb();
        var (lib, seriesId) = SeedSeries(temp, standalone: true);
        lib.UpsertVideo(seriesId, @"C:\V\S\only.mp4", 1, ".mp4");

        lib.GetNextEpisode(seriesId, 1).ShouldBeNull();
    }

    [Fact]
    public void GetNextEpisode_skips_gaps_in_numbering()
    {
        using var temp = new TempDb();
        var (lib, seriesId) = SeedSeries(temp, standalone: false);
        lib.UpsertVideo(seriesId, @"C:\V\S\a.mp4", 1, ".mp4");
        lib.UpsertVideo(seriesId, @"C:\V\S\c.mp4", 5, ".mp4");

        var next = lib.GetNextEpisode(seriesId, 1);

        next.ShouldNotBeNull();
        next!.EpisodeNo.ShouldBe(5);
    }
}
