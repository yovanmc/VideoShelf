using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Shouldly;
using VideoShelf.App.Services;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.Tests;

public class ResolutionBackfillServiceTests
{
    [Fact]
    public async Task BackfillAsync_writes_resolution_for_needing_rows()
    {
        using var temp = new AppTempDb();
        var library = new LibraryRepository(temp.Db);

        var sourceId = library.UpsertSource(@"C:\Videos", "Videos");
        var sectionId = library.UpsertSection(sourceId, "Creator A");
        var seriesId = library.UpsertSeries(sectionId, "Cool Story", isStandalone: false);
        var videoId = library.UpsertVideo(seriesId, @"C:\Videos\Creator A\ep1.mp4", episodeNo: 1, format: ".mp4");

        library.GetVideosNeedingResolution().Count.ShouldBe(1);

        var fake = new FakeMediaProbe
        {
            Result = new MediaProbeResult(null, System.Array.Empty<ChapterRecord>(), Width: 1280, Height: 720)
        };

        var svc = new ResolutionBackfillService(library, fake);
        await svc.BackfillAsync(CancellationToken.None);

        library.GetVideosNeedingResolution().ShouldBeEmpty();
    }

    [Fact]
    public async Task BackfillAsync_skips_when_probe_returns_null_dimensions()
    {
        using var temp = new AppTempDb();
        var library = new LibraryRepository(temp.Db);

        var sourceId = library.UpsertSource(@"C:\Videos", "Videos");
        var sectionId = library.UpsertSection(sourceId, "Creator A");
        var seriesId = library.UpsertSeries(sectionId, "Cool Story", isStandalone: false);
        library.UpsertVideo(seriesId, @"C:\Videos\Creator A\ep1.mp4", episodeNo: 1, format: ".mp4");

        var fake = new FakeMediaProbe
        {
            Result = new MediaProbeResult(null, System.Array.Empty<ChapterRecord>(), null, null)
        };

        var svc = new ResolutionBackfillService(library, fake);
        await svc.BackfillAsync(CancellationToken.None);

        // Row still needs resolution (probe returned null)
        library.GetVideosNeedingResolution().Count.ShouldBe(1);
    }

    [Fact]
    public async Task BackfillAsync_reruns_are_noop_once_filled()
    {
        using var temp = new AppTempDb();
        var library = new LibraryRepository(temp.Db);

        var sourceId = library.UpsertSource(@"C:\Videos", "Videos");
        var sectionId = library.UpsertSection(sourceId, "Creator A");
        var seriesId = library.UpsertSeries(sectionId, "Cool Story", isStandalone: false);
        library.UpsertVideo(seriesId, @"C:\Videos\Creator A\ep1.mp4", episodeNo: 1, format: ".mp4");

        var fake = new FakeMediaProbe
        {
            Result = new MediaProbeResult(null, System.Array.Empty<ChapterRecord>(), Width: 3840, Height: 2160)
        };

        var svc = new ResolutionBackfillService(library, fake);
        await svc.BackfillAsync(CancellationToken.None);
        library.GetVideosNeedingResolution().ShouldBeEmpty();

        // Re-run: still empty (already filled)
        await svc.BackfillAsync(CancellationToken.None);
        library.GetVideosNeedingResolution().ShouldBeEmpty();
    }

    [Fact]
    public async Task BackfillAsync_cancellation_rethrows()
    {
        using var temp = new AppTempDb();
        var library = new LibraryRepository(temp.Db);

        var sourceId = library.UpsertSource(@"C:\Videos", "Videos");
        var sectionId = library.UpsertSection(sourceId, "Creator A");
        var seriesId = library.UpsertSeries(sectionId, "Cool Story", isStandalone: false);
        library.UpsertVideo(seriesId, @"C:\Videos\Creator A\ep1.mp4", episodeNo: 1, format: ".mp4");

        var cts = new CancellationTokenSource();
        cts.Cancel();

        var svc = new ResolutionBackfillService(library, new FakeMediaProbe());
        await Should.ThrowAsync<OperationCanceledException>(
            () => svc.BackfillAsync(cts.Token));
    }
}
