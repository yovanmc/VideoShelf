using System.Linq;
using System.Threading.Tasks;
using Shouldly;
using VideoShelf.App.Services;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Models;
using VideoShelf.Core.Scanning;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;
using System.Threading;

namespace VideoShelf.App.Tests;

public class LibraryViewModelTests
{
    private sealed class NullThumbs : IThumbnailService
    {
        public Task<string?> GetThumbnailPathAsync(string videoPath, CancellationToken ct)
            => Task.FromResult<string?>(null);
    }

    private static LibraryViewModel Build(AppTempDb temp, TempDir dir)
    {
        dir.Touch("Creator A/Cool Story.mp4");
        dir.Touch("Creator A/Cool Story 2.mp4");
        dir.Touch("Travel Vlogs/Iceland.mkv");
        var lib = new LibraryRepository(temp.Db);
        var watch = new WatchRepository(temp.Db);
        new ScanService(temp.Db, lib).ScanSource(dir.Path, "V");
        return new LibraryViewModel(lib, watch, new NullThumbs());
    }

    [Fact]
    public async Task LoadSections_lists_all_sections_sorted_by_name()
    {
        using var temp = new AppTempDb();
        using var dir = new TempDir();
        var vm = Build(temp, dir);

        await vm.LoadSectionsAsync();

        vm.Sections.Select(s => s.DisplayName).ShouldBe(new[] { "Creator A", "Travel Vlogs" });
    }

    [Fact]
    public async Task SelectingSection_loads_its_series()
    {
        using var temp = new AppTempDb();
        using var dir = new TempDir();
        var vm = Build(temp, dir);
        await vm.LoadSectionsAsync();

        await vm.SelectSectionAsync(vm.Sections.Single(s => s.DisplayName == "Creator A"));

        vm.SelectedSection!.SeriesList.Single().BaseTitle.ShouldBe("Cool Story");
    }

    [Fact]
    public async Task ChangingSort_reloads_open_section()
    {
        using var temp = new AppTempDb();
        using var dir = new TempDir();
        var vm = Build(temp, dir);
        await vm.LoadSectionsAsync();
        await vm.SelectSectionAsync(vm.Sections.First());

        vm.SortMode = BrowseSort.DateAdded; // triggers reload

        // allow the async reload kicked off by the setter to complete
        await vm.WaitForIdleAsync();
        vm.SelectedSection!.SeriesList.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Search_populates_results_and_clear_empties_them()
    {
        using var temp = new AppTempDb();
        using var dir = new TempDir();
        var vm = Build(temp, dir);
        await vm.LoadSectionsAsync();

        vm.SearchText = "iceland";
        await vm.WaitForIdleAsync();
        vm.SearchResults.ShouldContain(h => h.Title == "Travel Vlogs" || h.Title.Contains("Iceland"));

        vm.SearchText = "";
        await vm.WaitForIdleAsync();
        vm.SearchResults.ShouldBeEmpty();
    }
}
