using System.Linq;
using Shouldly;
using VideoShelf.Core.Models;
using VideoShelf.Core.Scanning;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;

namespace VideoShelf.Core.Tests.Storage;

public class SearchTests
{
    private static LibraryRepository Seed(TempDb temp, TempDir dir)
    {
        dir.Touch("Creator A/Cool Story.mp4");
        dir.Touch("Creator A/Cool Story 2.mp4");
        dir.Touch("Travel Vlogs/Iceland Trip.mkv");
        var lib = new LibraryRepository(temp.Db);
        new ScanService(temp.Db, lib).ScanSource(dir.Path, "My Videos");
        return lib;
    }

    [Fact]
    public void Search_matches_section_series_and_video_titles_case_insensitively()
    {
        using var temp = new TempDb();
        using var dir = new TempDir();
        var lib = Seed(temp, dir);

        var results = lib.Search("cool");

        results.Any(r => r.Kind == SearchHitKind.Series && r.Title == "Cool Story").ShouldBeTrue();
        results.All(r => r.Title.Contains("Cool", System.StringComparison.OrdinalIgnoreCase)
                         || r.Kind == SearchHitKind.Video).ShouldBeTrue();
    }

    [Fact]
    public void Search_matches_section_name()
    {
        using var temp = new TempDb();
        using var dir = new TempDir();
        var lib = Seed(temp, dir);

        var results = lib.Search("travel");

        results.ShouldContain(r => r.Kind == SearchHitKind.Section && r.Title == "Travel Vlogs");
    }

    [Fact]
    public void Search_blank_query_returns_empty()
    {
        using var temp = new TempDb();
        using var dir = new TempDir();
        var lib = Seed(temp, dir);

        lib.Search("   ").ShouldBeEmpty();
    }
}
