using System.Threading;
using System.Threading.Tasks;
using Shouldly;
using VideoShelf.App.Scale;
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

    [Fact]
    public async Task Backfill_probes_all_pending_in_parallel_and_persists_each()
    {
        using var temp = new AppTempDb();
        var library = new LibraryRepository(temp.Db);

        // Seed 60 videos across 3 creators × 5 series (using stress seeder)
        new StressLibrarySeeder(library).Seed(StressLibrarySpec.Generate(3, 5, 60, seed: 2), @"C:\s");

        // Fake probe with delay so concurrent calls actually overlap
        var fakeProbe = new FakeMediaProbe(durationSeconds: 100, width: 1280, height: 720);

        var (settings, settingsDb) = FakeSettings.WithProbeConcurrency(4);
        using (settingsDb)
        {
            var svc = new MediaBackfillService(library, fakeProbe, settings);
            await svc.BackfillAsync(CancellationToken.None);
        }

        // All 60 videos must have a duration written (independent commits, crash-safe)
        library.CountVideosWithDuration().ShouldBe(60);

        // Parallelism was actually engaged (peak > 1 means at least 2 concurrent probes ran)
        fakeProbe.MaxObservedConcurrency.ShouldBeGreaterThan(1);
    }
}
