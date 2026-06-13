using Shouldly;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;

namespace VideoShelf.Core.Tests.Storage;

public class ItemArtRepositoryTests
{
    // ── helpers ────────────────────────────────────────────────────────────

    private static (LibraryRepository lib, ItemArtRepository art, long srcId, long seriesId, long videoId)
        Seed(TempDb temp)
    {
        var lib = new LibraryRepository(temp.Db);
        var art = new ItemArtRepository(temp.Db);
        var srcId = lib.UpsertSource(@"C:\V", "V");
        var secId = lib.UpsertSection(srcId, "Creator A");
        var seriesId = lib.UpsertSeries(secId, "Show", false);
        var videoId = lib.UpsertVideo(seriesId, @"C:\V\Show\e01.mp4", 1, "mp4");
        return (lib, art, srcId, seriesId, videoId);
    }

    // ── video_art ──────────────────────────────────────────────────────────

    [Fact]
    public void GetVideoArt_returns_null_when_not_set()
    {
        using var temp = new TempDb();
        var (_, art, _, _, videoId) = Seed(temp);
        art.GetVideoArt(videoId).ShouldBeNull();
    }

    [Fact]
    public void Video_art_round_trips()
    {
        using var temp = new TempDb();
        var (_, art, _, _, videoId) = Seed(temp);

        art.SetVideoArt(videoId, @"C:\covers\cover.png");

        art.GetVideoArt(videoId).ShouldBe(@"C:\covers\cover.png");
    }

    [Fact]
    public void SetVideoArt_upsert_overwrites_previous_value()
    {
        using var temp = new TempDb();
        var (_, art, _, _, videoId) = Seed(temp);

        art.SetVideoArt(videoId, @"C:\covers\first.png");
        art.SetVideoArt(videoId, @"C:\covers\second.png");

        art.GetVideoArt(videoId).ShouldBe(@"C:\covers\second.png");
    }

    [Fact]
    public void ClearVideoArt_removes_the_override()
    {
        using var temp = new TempDb();
        var (_, art, _, _, videoId) = Seed(temp);

        art.SetVideoArt(videoId, @"C:\covers\cover.png");
        art.ClearVideoArt(videoId);

        art.GetVideoArt(videoId).ShouldBeNull();
    }

    [Fact]
    public void VideoArt_cascades_on_video_delete()
    {
        using var temp = new TempDb();
        var (lib, art, srcId, _, videoId) = Seed(temp);

        art.SetVideoArt(videoId, @"C:\covers\cover.png");
        // Deleting the source cascades: source → sections → series → videos → video_art
        lib.RemoveSource(srcId);

        art.GetVideoArt(videoId).ShouldBeNull();
    }

    // ── series_art ─────────────────────────────────────────────────────────

    [Fact]
    public void GetSeriesArt_returns_null_when_not_set()
    {
        using var temp = new TempDb();
        var (_, art, _, seriesId, _) = Seed(temp);
        art.GetSeriesArt(seriesId).ShouldBeNull();
    }

    [Fact]
    public void Series_art_round_trips()
    {
        using var temp = new TempDb();
        var (_, art, _, seriesId, _) = Seed(temp);

        art.SetSeriesArt(seriesId, @"C:\covers\series.png");

        art.GetSeriesArt(seriesId).ShouldBe(@"C:\covers\series.png");
    }

    [Fact]
    public void SetSeriesArt_upsert_overwrites_previous_value()
    {
        using var temp = new TempDb();
        var (_, art, _, seriesId, _) = Seed(temp);

        art.SetSeriesArt(seriesId, @"C:\covers\first.png");
        art.SetSeriesArt(seriesId, @"C:\covers\second.png");

        art.GetSeriesArt(seriesId).ShouldBe(@"C:\covers\second.png");
    }

    [Fact]
    public void ClearSeriesArt_removes_the_override()
    {
        using var temp = new TempDb();
        var (_, art, _, seriesId, _) = Seed(temp);

        art.SetSeriesArt(seriesId, @"C:\covers\series.png");
        art.ClearSeriesArt(seriesId);

        art.GetSeriesArt(seriesId).ShouldBeNull();
    }

    [Fact]
    public void SeriesArt_cascades_on_series_delete()
    {
        using var temp = new TempDb();
        var (lib, art, srcId, seriesId, _) = Seed(temp);

        art.SetSeriesArt(seriesId, @"C:\covers\series.png");
        // Deleting the source cascades: source → sections → series → series_art
        lib.RemoveSource(srcId);

        art.GetSeriesArt(seriesId).ShouldBeNull();
    }
}
