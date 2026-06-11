using System.IO;
using System.Linq;
using System.Threading;
using Shouldly;
using VideoShelf.Core.Models;
using VideoShelf.Core.Scanning;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;

namespace VideoShelf.Core.Tests.Storage;

public class SortTests
{
    [Fact]
    public void GetSeriesSummaries_sorts_by_name()
    {
        using var temp = new TempDb();
        using var dir = new TempDir();
        dir.Touch("Sec/Banana.mp4");
        dir.Touch("Sec/Apple.mp4");
        var lib = new LibraryRepository(temp.Db);
        new ScanService(temp.Db, lib).ScanSource(dir.Path, "V");
        var sectionId = lib.GetSectionSummaries().Single().SectionId;

        var byName = lib.GetSeriesSummaries(sectionId, BrowseSort.Name);

        byName.Select(s => s.BaseTitle).ShouldBe(new[] { "Apple", "Banana" });
    }

    [Fact]
    public void GetSeriesSummaries_sorts_by_recently_watched_first()
    {
        using var temp = new TempDb();
        using var dir = new TempDir();
        dir.Touch("Sec/Apple.mp4");
        dir.Touch("Sec/Banana.mp4");
        var lib = new LibraryRepository(temp.Db);
        var watch = new WatchRepository(temp.Db);
        new ScanService(temp.Db, lib).ScanSource(dir.Path, "V");
        var sectionId = lib.GetSectionSummaries().Single().SectionId;

        // Watch Banana's episode -> Banana should sort first under RecentlyWatched.
        var banana = lib.GetSeriesSummaries(sectionId).Single(s => s.BaseTitle == "Banana");
        var bananaEp = lib.GetEpisodes(banana.SeriesId).First();
        watch.SetWatched(bananaEp.VideoId, true);

        var byWatched = lib.GetSeriesSummaries(sectionId, BrowseSort.RecentlyWatched);

        byWatched.First().BaseTitle.ShouldBe("Banana");
    }
}
