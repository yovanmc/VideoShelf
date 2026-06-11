using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Shouldly;
using VideoShelf.App.Services;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.Core.Scanning;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;

namespace VideoShelf.App.Tests;

public class ScanCoordinatorTests
{
    [Fact]
    public async Task ScanAll_indexes_every_source()
    {
        using var temp = new AppTempDb();
        using var dirA = new TempDir();
        using var dirB = new TempDir();
        dirA.Touch("Creator A/Cool Story.mp4");
        dirB.Touch("Vlogs/Trip.mkv");

        var lib = new LibraryRepository(temp.Db);
        lib.UpsertSource(dirA.Path, "A");
        lib.UpsertSource(dirB.Path, "B");

        var scan = new ScanService(temp.Db, lib);
        var coordinator = new ScanCoordinator(lib, scan);

        await coordinator.ScanAllAsync(CancellationToken.None);

        lib.GetSectionSummaries().Select(s => s.DisplayName).OrderBy(n => n)
            .ShouldBe(new[] { "Creator A", "Vlogs" });
    }

    [Fact]
    public async Task ScanAll_reports_not_busy_after_completion()
    {
        using var temp = new AppTempDb();
        var lib = new LibraryRepository(temp.Db);
        var coordinator = new ScanCoordinator(lib, new ScanService(temp.Db, lib));

        await coordinator.ScanAllAsync(CancellationToken.None);

        coordinator.IsBusy.ShouldBeFalse();
    }
}
