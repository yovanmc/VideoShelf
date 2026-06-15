using System.Linq;
using Shouldly;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;

namespace VideoShelf.Core.Tests.Storage;

/// <summary>
/// Verifies that GetEpisodes and GetEpisode carry duration and resume_position
/// through to EpisodeView (Group C1).
/// </summary>
public class EpisodeDurationTests
{
    private static (LibraryRepository lib, long seriesId, long videoId1, long videoId2) Seed(TempDb temp)
    {
        var lib = new LibraryRepository(temp.Db);
        var sourceId = lib.UpsertSource(@"C:\V", "V");
        var sectionId = lib.UpsertSection(sourceId, "Creator");
        var seriesId = lib.UpsertSeries(sectionId, "Show", isStandalone: false);
        var vid1 = lib.UpsertVideo(seriesId, @"C:\V\Creator\Show.mp4", episodeNo: 1, format: ".mp4");
        var vid2 = lib.UpsertVideo(seriesId, @"C:\V\Creator\Show 2.mp4", episodeNo: 2, format: ".mp4");
        return (lib, seriesId, vid1, vid2);
    }

    [Fact]
    public void GetEpisodes_carries_duration_and_resume_position()
    {
        using var temp = new TempDb();
        var (lib, seriesId, vid1, _) = Seed(temp);

        lib.SetDuration(vid1, 3600.0);
        lib.SetResumePosition(vid1, 900.0);

        var eps = lib.GetEpisodes(seriesId);

        var ep = eps.Single(e => e.VideoId == vid1);
        ep.Duration.ShouldNotBeNull();
        ep.Duration!.Value.ShouldBe(3600.0, tolerance: 0.001);
        ep.ResumePosition.ShouldBe(900.0, tolerance: 0.001);
    }

    [Fact]
    public void GetEpisodes_returns_null_duration_when_not_yet_probed()
    {
        using var temp = new TempDb();
        var (lib, seriesId, _, vid2) = Seed(temp);

        // vid2 has no duration set
        var eps = lib.GetEpisodes(seriesId);

        var ep = eps.Single(e => e.VideoId == vid2);
        ep.Duration.ShouldBeNull();
        ep.ResumePosition.ShouldBe(0.0);
    }

    [Fact]
    public void GetEpisode_carries_duration_and_resume_position()
    {
        using var temp = new TempDb();
        var (lib, _, vid1, _) = Seed(temp);

        lib.SetDuration(vid1, 1800.0);
        lib.SetResumePosition(vid1, 300.0);

        var ep = lib.GetEpisode(vid1);

        ep.ShouldNotBeNull();
        ep!.Duration.ShouldNotBeNull();
        ep.Duration!.Value.ShouldBe(1800.0, tolerance: 0.001);
        ep.ResumePosition.ShouldBe(300.0, tolerance: 0.001);
    }

    [Fact]
    public void GetEpisode_returns_null_duration_when_not_yet_probed()
    {
        using var temp = new TempDb();
        var (lib, _, vid1, _) = Seed(temp);

        // No duration set
        var ep = lib.GetEpisode(vid1);

        ep.ShouldNotBeNull();
        ep!.Duration.ShouldBeNull();
        ep.ResumePosition.ShouldBe(0.0);
    }
}
