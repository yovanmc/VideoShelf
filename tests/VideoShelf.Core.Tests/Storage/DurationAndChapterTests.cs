using System.Collections.Generic;
using System.Linq;
using Shouldly;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;

namespace VideoShelf.Core.Tests.Storage;

public class DurationAndChapterTests
{
    // Helper: seed source→section→series→video, return video id.
    private static long SeedVideo(LibraryRepository repo, string filePath)
    {
        var sourceId = repo.UpsertSource(@"C:\Vids", "Vids");
        var sectionId = repo.UpsertSection(sourceId, "Creator A");
        var seriesId = repo.UpsertSeries(sectionId, "Cool Story", isStandalone: false);
        return repo.UpsertVideo(seriesId, filePath, episodeNo: 1, format: ".mp4");
    }

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

    [Fact]
    public void ReplaceChapters_then_GetChapters_roundtrips_and_replaces()
    {
        using var temp = new TempDb();
        var repo = new LibraryRepository(temp.Db);

        var sourceId = repo.UpsertSource(@"C:\Vids", "Vids");
        var sectionId = repo.UpsertSection(sourceId, "Creator A");
        var seriesId = repo.UpsertSeries(sectionId, "Cool Story", isStandalone: false);
        var videoId = repo.UpsertVideo(seriesId, @"C:\Vids\Creator A\ep1.mp4", episodeNo: 1, format: ".mp4");

        // Write 3 chapters and verify round-trip
        var first = new List<ChapterRecord>
        {
            new(0, "Intro",   0.0),
            new(1, "Act One", 60.0),
            new(2, "Credits", 3540.0),
        };
        repo.ReplaceChapters(videoId, first);

        var read1 = repo.GetChapters(videoId);
        read1.Count.ShouldBe(3);
        read1[0].ShouldBe(new ChapterRecord(0, "Intro",   0.0));
        read1[1].ShouldBe(new ChapterRecord(1, "Act One", 60.0));
        read1[2].ShouldBe(new ChapterRecord(2, "Credits", 3540.0));

        // Replace with 2 different chapters — old ones must be gone
        var second = new List<ChapterRecord>
        {
            new(0, "Part A", 0.0),
            new(1, "Part B", 120.0),
        };
        repo.ReplaceChapters(videoId, second);

        var read2 = repo.GetChapters(videoId);
        read2.Count.ShouldBe(2);
        read2[0].ShouldBe(new ChapterRecord(0, "Part A", 0.0));
        read2[1].ShouldBe(new ChapterRecord(1, "Part B", 120.0));
    }
}
