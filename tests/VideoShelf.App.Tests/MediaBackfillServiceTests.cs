using System.Threading;
using System.Threading.Tasks;
using Shouldly;
using VideoShelf.App.Services;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.Tests;

public class MediaBackfillServiceTests
{
    [Fact]
    public async Task BackfillAsync_populates_duration_and_resolution()
    {
        using var temp = new AppTempDb();
        var library = new LibraryRepository(temp.Db);

        // Seed source → section → series → video (null duration by default)
        var sourceId = library.UpsertSource(@"C:\Videos", "Videos");
        var sectionId = library.UpsertSection(sourceId, "Creator A");
        var seriesId = library.UpsertSeries(sectionId, "Cool Story", isStandalone: false);
        library.UpsertVideo(seriesId, @"C:\Videos\Creator A\ep1.mp4", episodeNo: 1, format: ".mp4");

        // Confirm it starts as needing duration
        library.GetVideosNeedingDuration().Count.ShouldBe(1);

        var fake = new FakeMediaProbe
        {
            Result = new MediaProbeResult(120.0, Width: 1920, Height: 1080)
        };

        var svc = new MediaBackfillService(library, fake);
        await svc.BackfillAsync(CancellationToken.None);

        // Duration stored → no longer needs backfill
        library.GetVideosNeedingDuration().ShouldBeEmpty();

        // Resolution also stored (Width/Height present in probe result)
        library.GetVideosNeedingResolution().ShouldBeEmpty();

        // Re-running is a no-op: still 0 pending
        await svc.BackfillAsync(CancellationToken.None);
        library.GetVideosNeedingDuration().ShouldBeEmpty();
    }
}
