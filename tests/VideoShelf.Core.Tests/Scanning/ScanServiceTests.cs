using System.Linq;
using Shouldly;
using VideoShelf.Core.Scanning;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;

namespace VideoShelf.Core.Tests.Scanning;

public class ScanServiceTests
{
    [Fact]
    public void Scanning_a_source_persists_sections_series_and_videos()
    {
        using var temp = new TempDb();
        using var dir = new TempDir();
        dir.Touch("Creator A/Cool Story.mp4");
        dir.Touch("Creator A/Cool Story 2.mp4");
        dir.Touch("Home Videos/Trip.mkv");

        var lib = new LibraryRepository(temp.Db);
        var scan = new ScanService(temp.Db, lib);

        scan.ScanSource(dir.Path, "My Videos");

        var sourceId = lib.GetSources().Single().Id;
        // Creator A has one series ("Cool Story") with 2 episodes; Home Videos has one standalone.
        var sections = lib.GetSections(sourceId).OrderBy(s => s.FolderName).ToList();
        sections.Select(s => s.FolderName).ShouldBe(new[] { "Creator A", "Home Videos" });

        var creatorSeries = lib.GetSeriesForSection(sections[0].Id).Single();
        creatorSeries.BaseTitle.ShouldBe("Cool Story");
        creatorSeries.IsStandalone.ShouldBeFalse();
        lib.GetVideosForSeries(creatorSeries.Id).Count.ShouldBe(2);

        var homeSeries = lib.GetSeriesForSection(sections[1].Id).Single();
        homeSeries.IsStandalone.ShouldBeTrue();
    }

    [Fact]
    public void Rescan_is_idempotent_and_preserves_watched_state()
    {
        using var temp = new TempDb();
        using var dir = new TempDir();
        dir.Touch("Creator A/Cool Story.mp4");

        var lib = new LibraryRepository(temp.Db);
        var watch = new WatchRepository(temp.Db);
        var scan = new ScanService(temp.Db, lib);

        scan.ScanSource(dir.Path, "My Videos");
        var sourceId = lib.GetSources().Single().Id;
        var section = lib.GetSections(sourceId).Single();
        var series = lib.GetSeriesForSection(section.Id).Single();
        var video = lib.GetVideosForSeries(series.Id).Single();
        watch.SetWatched(video.Id, true);

        scan.ScanSource(dir.Path, "My Videos"); // rescan

        // Still exactly one video, watched flag intact.
        var after = lib.GetVideosForSeries(series.Id).Single();
        after.Id.ShouldBe(video.Id);
        after.Watched.ShouldBeTrue();
    }
}
