using System.IO;
using System.Linq;
using Shouldly;
using VideoShelf.Core.Scanning;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;

namespace VideoShelf.Core.Tests.Storage;

public class BrowseQueryTests
{
    private static (LibraryRepository lib, WatchRepository watch, long sectionId) Seed(TempDb temp, TempDir dir)
    {
        // Creator A: a 2-episode series + a standalone. Home Videos: 1 standalone.
        dir.Touch("Creator A/Cool Story.mp4");
        dir.Touch("Creator A/Cool Story 2.mp4");
        dir.Touch("Creator A/One Off.mp4");
        dir.Touch("Home Videos/Trip.mkv");

        var lib = new LibraryRepository(temp.Db);
        var watch = new WatchRepository(temp.Db);
        new ScanService(temp.Db, lib).ScanSource(dir.Path, "My Videos");

        var sourceId = lib.GetSources().Single().Id;
        var sectionId = lib.GetSections(sourceId)
            .Single(s => s.FolderName == "Creator A").Id;
        return (lib, watch, sectionId);
    }

    [Fact]
    public void GetSectionSummaries_returns_unwatched_aggregate_per_section()
    {
        using var temp = new TempDb();
        using var dir = new TempDir();
        var (lib, _, _) = Seed(temp, dir);

        var sections = lib.GetSectionSummaries().OrderBy(s => s.DisplayName).ToList();

        sections.Select(s => s.DisplayName).ShouldBe(new[] { "Creator A", "Home Videos" });
        // Creator A has 3 videos, all unwatched.
        sections.Single(s => s.DisplayName == "Creator A").UnwatchedCount.ShouldBe(3);
        sections.Single(s => s.DisplayName == "Home Videos").UnwatchedCount.ShouldBe(1);
    }

    [Fact]
    public void GetSeriesSummaries_carries_standalone_flag_unwatched_and_thumb_seed()
    {
        using var temp = new TempDb();
        using var dir = new TempDir();
        var (lib, watch, sectionId) = Seed(temp, dir);

        var coolStoryVideos = lib.GetSeriesSummaries(sectionId);
        var cool = coolStoryVideos.Single(s => s.BaseTitle == "Cool Story");
        cool.IsStandalone.ShouldBeFalse();
        cool.EpisodeCount.ShouldBe(2);
        cool.UnwatchedCount.ShouldBe(2);
        cool.ThumbnailSeedPath.ShouldEndWith("Cool Story.mp4"); // first episode

        var oneOff = coolStoryVideos.Single(s => s.BaseTitle == "One Off");
        oneOff.IsStandalone.ShouldBeTrue();

        // Mark episode 1 watched: unwatched count drops to 1.
        var ep1 = lib.GetEpisodes(cool.SeriesId).First();
        watch.SetWatched(ep1.VideoId, true);
        lib.GetSeriesSummaries(sectionId).Single(s => s.BaseTitle == "Cool Story")
            .UnwatchedCount.ShouldBe(1);
    }

    [Fact]
    public void GetEpisodes_returns_rows_with_watched_and_missing()
    {
        using var temp = new TempDb();
        using var dir = new TempDir();
        var (lib, _, sectionId) = Seed(temp, dir);
        var cool = lib.GetSeriesSummaries(sectionId).Single(s => s.BaseTitle == "Cool Story");

        var eps = lib.GetEpisodes(cool.SeriesId);

        eps.Count.ShouldBe(2);
        eps.Select(e => e.EpisodeNo).ShouldBe(new[] { 1, 2 });
        eps.All(e => !e.Watched).ShouldBeTrue();
        eps.All(e => !e.Missing).ShouldBeTrue();
        eps.First().Title.ShouldBe("Cool Story");
    }
}
