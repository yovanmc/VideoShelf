using Shouldly;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;

namespace VideoShelf.Core.Tests.Storage;

public class CreatorArtRepositoryTests
{
    [Fact]
    public void Get_returns_null_when_no_override_set()
    {
        using var temp = new TempDb();
        var art = new CreatorArtRepository(temp.Db);

        art.GetArtPath(42).ShouldBeNull();
    }

    [Fact]
    public void Set_then_Get_round_trips_and_Set_overwrites()
    {
        using var temp = new TempDb();
        var lib = new LibraryRepository(temp.Db);
        var art = new CreatorArtRepository(temp.Db);
        var srcId = lib.UpsertSource(@"C:\V", "V");
        var sectionId = lib.UpsertSection(srcId, "Creator A");

        art.SetArtPath(sectionId, @"C:\pics\a.png");
        art.GetArtPath(sectionId).ShouldBe(@"C:\pics\a.png");

        art.SetArtPath(sectionId, @"C:\pics\b.jpg");
        art.GetArtPath(sectionId).ShouldBe(@"C:\pics\b.jpg");
    }

    [Fact]
    public void Clear_removes_the_override()
    {
        using var temp = new TempDb();
        var lib = new LibraryRepository(temp.Db);
        var art = new CreatorArtRepository(temp.Db);
        var srcId = lib.UpsertSource(@"C:\V", "V");
        var sectionId = lib.UpsertSection(srcId, "Creator A");

        art.SetArtPath(sectionId, @"C:\pics\a.png");
        art.ClearArtPath(sectionId);

        art.GetArtPath(sectionId).ShouldBeNull();
    }
}
