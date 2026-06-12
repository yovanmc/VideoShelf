using System.Linq;
using Shouldly;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;

namespace VideoShelf.Core.Tests.Storage;

public class LibraryRepositoryTests
{
    [Fact]
    public void AddSource_is_idempotent_by_path()
    {
        using var temp = new TempDb();
        var repo = new LibraryRepository(temp.Db);

        var id1 = repo.UpsertSource(@"C:\Vids", "Vids");
        var id2 = repo.UpsertSource(@"C:\Vids", "Vids");

        id1.ShouldBe(id2);
        repo.GetSources().Count.ShouldBe(1);
    }

    [Fact]
    public void Upsert_section_series_video_round_trips()
    {
        using var temp = new TempDb();
        var repo = new LibraryRepository(temp.Db);

        var sourceId = repo.UpsertSource(@"C:\Vids", "Vids");
        var sectionId = repo.UpsertSection(sourceId, "Creator A");
        var seriesId = repo.UpsertSeries(sectionId, "Cool Story", isStandalone: false);
        repo.UpsertVideo(seriesId, @"C:\Vids\Creator A\Cool Story.mp4", episodeNo: 1, format: ".mp4");

        var videos = repo.GetVideosForSeries(seriesId);
        videos.Single().FilePath.ShouldBe(@"C:\Vids\Creator A\Cool Story.mp4");
        videos.Single().EpisodeNo.ShouldBe(1);
        videos.Single().Watched.ShouldBeFalse();
    }

    [Fact]
    public void Upsert_video_updates_episode_without_duplicating()
    {
        using var temp = new TempDb();
        var repo = new LibraryRepository(temp.Db);
        var seriesId = repo.UpsertSeries(repo.UpsertSection(repo.UpsertSource(@"C:\V", "V"), "S"), "Base", false);

        repo.UpsertVideo(seriesId, @"C:\V\S\a.mp4", episodeNo: 1, format: ".mp4");
        repo.UpsertVideo(seriesId, @"C:\V\S\a.mp4", episodeNo: 2, format: ".mp4");

        var v = repo.GetVideosForSeries(seriesId).Single();
        v.EpisodeNo.ShouldBe(2);
    }

    [Fact]
    public void GetSection_returns_section_by_id()
    {
        using var temp = new TempDb();
        var repo = new LibraryRepository(temp.Db);
        var sourceId = repo.UpsertSource(@"C:\Vids", "Vids");
        var sectionId = repo.UpsertSection(sourceId, "Creator A");

        var section = repo.GetSection(sectionId);

        section.ShouldNotBeNull();
        section!.DisplayName.ShouldBe("Creator A");
        section.Id.ShouldBe(sectionId);
    }

    [Fact]
    public void GetSection_returns_null_for_missing_id()
    {
        using var temp = new TempDb();
        var repo = new LibraryRepository(temp.Db);

        var result = repo.GetSection(9999);

        result.ShouldBeNull();
    }

    [Fact]
    public void GetSectionSummaries_reports_total_video_count_and_a_seed_path()
    {
        using var temp = new TempDb();
        var repo = new LibraryRepository(temp.Db);

        var sourceId = repo.UpsertSource(@"C:\Vids", "Vids");
        var sectionId = repo.UpsertSection(sourceId, "Creator A");
        var seriesId = repo.UpsertSeries(sectionId, "Cool Story", isStandalone: false);
        repo.UpsertVideo(seriesId, @"C:\Vids\Creator A\Cool Story 1.mp4", episodeNo: 1, format: ".mp4");
        repo.UpsertVideo(seriesId, @"C:\Vids\Creator A\Cool Story 2.mp4", episodeNo: 2, format: ".mp4");

        var summary = repo.GetSectionSummaries().Single(s => s.SectionId == sectionId);

        summary.VideoCount.ShouldBe(2);
        summary.ThumbnailSeedPath.ShouldNotBeNull();
        summary.ThumbnailSeedPath!.ShouldEndWith(".mp4");
    }
}
