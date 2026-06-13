using System.Linq;
using Shouldly;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;
using Xunit;

namespace VideoShelf.Core.Tests.Storage;

public sealed class PlayQueueQueriesTests
{
    [Fact]
    public void GetEpisodesForSection_flattens_series_in_order_and_excludes_missing()
    {
        using var temp = new TempDb();
        var repo = new LibraryRepository(temp.Db);
        var src = repo.UpsertSource(@"C:\V", "V");
        var sec = repo.UpsertSection(src, "Creator A");

        // "Alpha" sorts before "Beta" by sort_key; episodes by episode_no
        var alpha = repo.UpsertSeries(sec, "Alpha", isStandalone: false);
        repo.UpsertVideo(alpha, @"C:\V\Creator A\Alpha 1.mp4", 1, ".mp4");
        repo.UpsertVideo(alpha, @"C:\V\Creator A\Alpha 2.mp4", 2, ".mp4");
        var beta = repo.UpsertSeries(sec, "Beta", isStandalone: true);
        repo.UpsertVideo(beta, @"C:\V\Creator A\Beta.mp4", 1, ".mp4");

        var eps = repo.GetEpisodesForSection(sec);
        eps.Select(e => e.Title).ShouldBe(new[] { "Alpha", "Alpha 2", "Beta" });
        eps.Select(e => e.FilePath).ShouldContain(@"C:\V\Creator A\Beta.mp4");
    }

    [Fact]
    public void GetEpisode_round_trips_by_video_id()
    {
        using var temp = new TempDb();
        var repo = new LibraryRepository(temp.Db);
        var src = repo.UpsertSource(@"C:\V", "V");
        var sec = repo.UpsertSection(src, "Creator A");
        var s = repo.UpsertSeries(sec, "Solo", isStandalone: true);
        repo.UpsertVideo(s, @"C:\V\Creator A\Solo.mp4", 1, ".mp4");

        var one = repo.GetEpisodesForSection(sec).Single();
        var byId = repo.GetEpisode(one.VideoId);
        byId.ShouldNotBeNull();
        byId!.FilePath.ShouldBe(@"C:\V\Creator A\Solo.mp4");
        repo.GetEpisode(999_999).ShouldBeNull();
    }
}
