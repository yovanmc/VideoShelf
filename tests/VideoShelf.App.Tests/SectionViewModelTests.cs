using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Shouldly;
using VideoShelf.App.Services;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Models;
using VideoShelf.Core.Scanning;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;

namespace VideoShelf.App.Tests;

public class SectionViewModelTests
{
    private sealed class NullThumbs : IThumbnailService
    {
        public Task<string?> GetThumbnailPathAsync(string videoPath, CancellationToken ct)
            => Task.FromResult<string?>(null);
    }

    [Fact]
    public async Task LoadSeries_populates_children_with_chosen_sort()
    {
        using var temp = new AppTempDb();
        using var dir = new TempDir();
        dir.Touch("Sec/Banana.mp4");
        dir.Touch("Sec/Apple.mp4");
        var lib = new LibraryRepository(temp.Db);
        var watch = new WatchRepository(temp.Db);
        new ScanService(temp.Db, lib).ScanSource(dir.Path, "V");
        var summary = lib.GetSectionSummaries().Single();
        var vm = new SectionViewModel(summary, lib, watch, new NullThumbs());

        await vm.LoadSeriesAsync(BrowseSort.Name, CancellationToken.None);

        vm.SeriesList.Select(s => s.BaseTitle).ShouldBe(new[] { "Apple", "Banana" });
        vm.DisplayName.ShouldBe("Sec");
        vm.UnwatchedCount.ShouldBe(2);
        vm.HasUnwatched.ShouldBeTrue();
    }
}
