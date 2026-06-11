using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Shouldly;
using VideoShelf.App.Services;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Scanning;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;

namespace VideoShelf.App.Tests;

public class SeriesViewModelTests
{
    private sealed class StubThumbnailService : IThumbnailService
    {
        public Task<string?> GetThumbnailPathAsync(string videoPath, CancellationToken ct)
            => Task.FromResult<string?>(@"C:\thumbs\seed.png");
    }

    private static (LibraryRepository lib, WatchRepository watch, long sectionId) Seed(AppTempDb temp, TempDir dir)
    {
        dir.Touch("Sec/Cool Story.mp4");
        dir.Touch("Sec/Cool Story 2.mp4");
        var lib = new LibraryRepository(temp.Db);
        var watch = new WatchRepository(temp.Db);
        new ScanService(temp.Db, lib).ScanSource(dir.Path, "V");
        var sectionId = lib.GetSectionSummaries().Single().SectionId;
        return (lib, watch, sectionId);
    }

    [Fact]
    public void UnwatchedBadge_shows_count_and_hides_when_fully_watched()
    {
        using var temp = new AppTempDb();
        using var dir = new TempDir();
        var (lib, watch, sectionId) = Seed(temp, dir);
        var summary = lib.GetSeriesSummaries(sectionId).Single();
        var vm = new SeriesViewModel(summary, lib, watch, new StubThumbnailService());

        vm.UnwatchedCount.ShouldBe(2);
        vm.HasUnwatched.ShouldBeTrue();

        // Watch both episodes, refresh.
        foreach (var e in lib.GetEpisodes(summary.SeriesId))
            watch.SetWatched(e.VideoId, true);
        vm.Refresh();

        vm.UnwatchedCount.ShouldBe(0);
        vm.HasUnwatched.ShouldBeFalse();
    }

    [Fact]
    public async Task LoadEpisodes_populates_child_viewmodels_in_order()
    {
        using var temp = new AppTempDb();
        using var dir = new TempDir();
        var (lib, watch, sectionId) = Seed(temp, dir);
        var summary = lib.GetSeriesSummaries(sectionId).Single();
        var vm = new SeriesViewModel(summary, lib, watch, new StubThumbnailService());

        await vm.LoadEpisodesAsync(CancellationToken.None);

        vm.Episodes.Select(e => e.EpisodeNo).ShouldBe(new[] { 1, 2 });
    }

    [Fact]
    public async Task LoadThumbnail_sets_path_from_service()
    {
        using var temp = new AppTempDb();
        using var dir = new TempDir();
        var (lib, watch, sectionId) = Seed(temp, dir);
        var summary = lib.GetSeriesSummaries(sectionId).Single();
        var vm = new SeriesViewModel(summary, lib, watch, new StubThumbnailService());

        await vm.LoadThumbnailAsync(CancellationToken.None);

        vm.ThumbnailPath.ShouldBe(@"C:\thumbs\seed.png");
    }
}
