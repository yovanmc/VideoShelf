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

public class BrowseFanoutTests
{
    private sealed class SeedThumbs : IThumbnailService
    {
        public Task<string?> GetThumbnailPathAsync(string videoPath, CancellationToken ct)
            => Task.FromResult<string?>(@"C:\thumbs\x.png");
    }

    private static (LibraryViewModel vm, LibraryRepository lib) Build(AppTempDb temp, TempDir dir)
    {
        dir.Touch("Sec/Cool Story.mp4");
        dir.Touch("Sec/Cool Story 2.mp4");
        var lib = new LibraryRepository(temp.Db);
        var watch = new WatchRepository(temp.Db);
        new ScanService(temp.Db, lib).ScanSource(dir.Path, "V");
        return (new LibraryViewModel(lib, watch, new SeedThumbs()), lib);
    }

    [Fact]
    public async Task SelectingSection_loads_series_with_episodes_and_thumbnails()
    {
        using var temp = new AppTempDb();
        using var dir = new TempDir();
        var (vm, _) = Build(temp, dir);
        await vm.LoadSectionsAsync();

        await vm.SelectSectionAsync(vm.Sections.Single());

        var series = vm.SelectedSection!.SeriesList.Single();
        series.Episodes.Select(e => e.EpisodeNo).ShouldBe(new[] { 1, 2 });
        series.ThumbnailPath.ShouldBe(@"C:\thumbs\x.png");
    }

    [Fact]
    public async Task TogglingEpisode_watched_updates_series_and_section_badges()
    {
        using var temp = new AppTempDb();
        using var dir = new TempDir();
        var (vm, _) = Build(temp, dir);
        await vm.LoadSectionsAsync();
        await vm.SelectSectionAsync(vm.Sections.Single());
        var section = vm.SelectedSection!;
        var series = section.SeriesList.Single();

        series.UnwatchedCount.ShouldBe(2);

        series.Episodes.First().ToggleWatchedCommand.Execute(null);

        series.UnwatchedCount.ShouldBe(1);
        section.UnwatchedCount.ShouldBe(1);
    }
}
