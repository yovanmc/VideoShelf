using Shouldly;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;

namespace VideoShelf.Core.Tests.Storage;

public class ResumePositionTests
{
    private static (LibraryRepository lib, long videoId) Seed(TempDb temp)
    {
        var lib = new LibraryRepository(temp.Db);
        var seriesId = lib.UpsertSeries(lib.UpsertSection(lib.UpsertSource(@"C:\V", "V"), "S"), "Base", false);
        var videoId = lib.UpsertVideo(seriesId, @"C:\V\S\a.mp4", 1, ".mp4");
        return (lib, videoId);
    }

    [Fact]
    public void New_video_has_null_resume_position()
    {
        using var temp = new TempDb();
        var (lib, videoId) = Seed(temp);

        lib.GetResumePosition(videoId).ShouldBeNull();
    }

    [Fact]
    public void SetResumePosition_persists_and_is_read_back()
    {
        using var temp = new TempDb();
        var (lib, videoId) = Seed(temp);

        lib.SetResumePosition(videoId, 123.5);

        lib.GetResumePosition(videoId).ShouldBe(123.5);
    }

    [Fact]
    public void SetResumePosition_overwrites_previous_value()
    {
        using var temp = new TempDb();
        var (lib, videoId) = Seed(temp);

        lib.SetResumePosition(videoId, 10.0);
        lib.SetResumePosition(videoId, 42.0);

        lib.GetResumePosition(videoId).ShouldBe(42.0);
    }

    [Fact]
    public void ClearResumePosition_sets_null()
    {
        using var temp = new TempDb();
        var (lib, videoId) = Seed(temp);

        lib.SetResumePosition(videoId, 99.0);
        lib.ClearResumePosition(videoId);

        lib.GetResumePosition(videoId).ShouldBeNull();
    }
}
