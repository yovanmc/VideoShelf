using System.IO;
using System.Linq;
using Shouldly;
using VideoShelf.Core.Scanning;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;

namespace VideoShelf.Core.Tests.Scanning;

public class MissingFileTests
{
    [Fact]
    public void Rescan_marks_deleted_file_missing_then_clears_when_restored()
    {
        using var temp = new TempDb();
        using var dir = new TempDir();
        var fileA = dir.Touch("Creator A/Cool Story.mp4");
        dir.Touch("Creator A/Cool Story 2.mp4");

        var lib = new LibraryRepository(temp.Db);
        var scan = new ScanService(temp.Db, lib);
        scan.ScanSource(dir.Path, "My Videos");

        var sourceId = lib.GetSources().Single().Id;
        var section = lib.GetSections(sourceId).Single();
        var series = lib.GetSeriesForSection(section.Id).Single();

        // Delete one episode file on disk, then rescan.
        File.Delete(fileA);
        scan.ScanSource(dir.Path, "My Videos");

        var afterDelete = lib.GetVideosForSeries(series.Id);
        afterDelete.Single(v => v.FilePath == fileA).Missing.ShouldBeTrue();
        afterDelete.Single(v => v.FilePath != fileA).Missing.ShouldBeFalse();

        // Restore the file, rescan: missing flag clears.
        dir.Touch("Creator A/Cool Story.mp4");
        scan.ScanSource(dir.Path, "My Videos");

        lib.GetVideosForSeries(series.Id)
            .Single(v => v.FilePath == fileA).Missing.ShouldBeFalse();
    }
}
