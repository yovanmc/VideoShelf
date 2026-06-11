using System.Linq;
using Shouldly;
using VideoShelf.Core.Scanning;
using VideoShelf.Core.Tests.TestSupport;

namespace VideoShelf.Core.Tests.Scanning;

public class FolderScannerTests
{
    [Fact]
    public void Scans_each_subfolder_as_a_section_with_only_video_files()
    {
        using var dir = new TempDir();
        dir.Touch("Creator A/skit.mp4");
        dir.Touch("Creator A/skit 2.mp4");
        dir.Touch("Creator A/notes.txt");          // ignored (not video)
        dir.Touch("Home Videos/trip.mkv");
        dir.Touch("loose.mp4");                      // file directly in root -> ignored (no section)

        var sections = FolderScanner.Scan(dir.Path).OrderBy(s => s.FolderName).ToList();

        sections.Count.ShouldBe(2);
        sections[0].FolderName.ShouldBe("Creator A");
        sections[0].Files.Select(f => f.FileName).OrderBy(x => x)
            .ShouldBe(new[] { "skit 2.mp4", "skit.mp4" });
        sections[1].FolderName.ShouldBe("Home Videos");
        sections[1].Files.Single().FileName.ShouldBe("trip.mkv");
    }

    [Fact]
    public void Empty_or_video_less_sections_are_omitted()
    {
        using var dir = new TempDir();
        dir.Touch("OnlyDocs/readme.txt");
        FolderScanner.Scan(dir.Path).ShouldBeEmpty();
    }
}
