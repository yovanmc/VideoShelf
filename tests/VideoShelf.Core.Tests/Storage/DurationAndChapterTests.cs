using System.Linq;
using Shouldly;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;

namespace VideoShelf.Core.Tests.Storage;

public class DurationAndChapterTests
{
    [Fact]
    public void GetVideosNeedingDuration_returns_only_null_duration_present_videos()
    {
        using var temp = new TempDb();
        var repo = new LibraryRepository(temp.Db);

        var sourceId = repo.UpsertSource(@"C:\Vids", "Vids");
        var sectionId = repo.UpsertSection(sourceId, "Creator A");
        var seriesId = repo.UpsertSeries(sectionId, "Cool Story", isStandalone: false);

        var id1 = repo.UpsertVideo(seriesId, @"C:\Vids\Creator A\ep1.mp4", episodeNo: 1, format: ".mp4");
        var id2 = repo.UpsertVideo(seriesId, @"C:\Vids\Creator A\ep2.mp4", episodeNo: 2, format: ".mp4");

        // Set duration on id1 only — id2 still needs probing
        repo.SetDuration(id1, 60.0);

        var needing = repo.GetVideosNeedingDuration();

        needing.Count.ShouldBe(1);
        needing.Single().Id.ShouldBe(id2);
        needing.Single().FilePath.ShouldBe(@"C:\Vids\Creator A\ep2.mp4");
    }

    [Fact]
    public void SetDuration_persists()
    {
        using var temp = new TempDb();
        var repo = new LibraryRepository(temp.Db);

        var sourceId = repo.UpsertSource(@"C:\Vids", "Vids");
        var sectionId = repo.UpsertSection(sourceId, "Creator A");
        var seriesId = repo.UpsertSeries(sectionId, "Cool Story", isStandalone: false);
        var videoId = repo.UpsertVideo(seriesId, @"C:\Vids\Creator A\ep1.mp4", episodeNo: 1, format: ".mp4");

        repo.SetDuration(videoId, 123.5);

        var video = repo.GetVideosForSeries(seriesId).Single();
        video.Duration.ShouldNotBeNull();
        video.Duration!.Value.ShouldBe(123.5, tolerance: 0.001);
    }
}
