using Shouldly;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;

namespace VideoShelf.Core.Tests.Storage;

public class RandomUnwatchedEpisodeTests
{
    private static (LibraryRepository lib, WatchRepository watch, long seriesId) Seed(TempDb temp)
    {
        var lib = new LibraryRepository(temp.Db);
        var watch = new WatchRepository(temp.Db);
        var sectionId = lib.UpsertSection(lib.UpsertSource(@"C:\V", "V"), "S");
        var seriesId = lib.UpsertSeries(sectionId, "ShowA", isStandalone: false);
        return (lib, watch, seriesId);
    }

    [Fact]
    public void Returns_null_when_library_is_empty()
    {
        using var temp = new TempDb();
        var lib = new LibraryRepository(temp.Db);

        lib.GetRandomUnwatchedEpisode().ShouldBeNull();
    }

    [Fact]
    public void Returns_null_when_all_videos_are_watched()
    {
        using var temp = new TempDb();
        var (lib, watch, seriesId) = Seed(temp);
        var id1 = lib.UpsertVideo(seriesId, @"C:\V\S\ep1.mp4", 1, ".mp4");
        var id2 = lib.UpsertVideo(seriesId, @"C:\V\S\ep2.mp4", 2, ".mp4");
        watch.SetWatched(id1, true);
        watch.SetWatched(id2, true);

        lib.GetRandomUnwatchedEpisode().ShouldBeNull();
    }

    [Fact]
    public void Returns_null_when_all_videos_are_missing()
    {
        using var temp = new TempDb();
        var (lib, _, seriesId) = Seed(temp);
        lib.UpsertVideo(seriesId, @"C:\V\S\ep1.mp4", 1, ".mp4");
        // Mark everything missing via the source id
        var sourceId = lib.GetSources()[0].Id;
        lib.MarkAllMissingForSource(sourceId);

        lib.GetRandomUnwatchedEpisode().ShouldBeNull();
    }

    [Fact]
    public void Returns_the_only_unwatched_episode_when_all_others_are_watched()
    {
        using var temp = new TempDb();
        var (lib, watch, seriesId) = Seed(temp);
        var id1 = lib.UpsertVideo(seriesId, @"C:\V\S\ep1.mp4", 1, ".mp4");
        var id2 = lib.UpsertVideo(seriesId, @"C:\V\S\ep2.mp4", 2, ".mp4");
        var id3 = lib.UpsertVideo(seriesId, @"C:\V\S\ep3.mp4", 3, ".mp4");
        watch.SetWatched(id1, true);
        watch.SetWatched(id3, true);

        var ep = lib.GetRandomUnwatchedEpisode();

        ep.ShouldNotBeNull();
        ep!.VideoId.ShouldBe(id2);
        ep.Watched.ShouldBeFalse();
        ep.Missing.ShouldBeFalse();
    }

    [Fact]
    public void Returns_an_unwatched_non_missing_episode_from_mixed_set()
    {
        using var temp = new TempDb();
        var (lib, watch, seriesId) = Seed(temp);
        var id1 = lib.UpsertVideo(seriesId, @"C:\V\S\ep1.mp4", 1, ".mp4");
        var id2 = lib.UpsertVideo(seriesId, @"C:\V\S\ep2.mp4", 2, ".mp4");
        watch.SetWatched(id1, true);
        // id2 is unwatched + present

        var ep = lib.GetRandomUnwatchedEpisode();

        ep.ShouldNotBeNull();
        ep!.Watched.ShouldBeFalse();
        ep.Missing.ShouldBeFalse();
    }
}
