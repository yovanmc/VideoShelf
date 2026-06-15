using System.IO;
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

public class SeriesViewModelTests
{
    private sealed class StubThumbnailService : IThumbnailService
    {
        public Task<string?> GetThumbnailPathAsync(string videoPath, CancellationToken ct)
            => Task.FromResult<string?>(@"C:\thumbs\seed.png");
    }

    private sealed class NullThumbs : IThumbnailService
    {
        public Task<string?> GetThumbnailPathAsync(string videoPath, CancellationToken ct)
            => Task.FromResult<string?>(null);
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

    /// <summary>Seed a section with one multi-episode series and one standalone series.</summary>
    private static (LibraryRepository lib, WatchRepository watch, SeriesSummary multiSeries, SeriesSummary standalone)
        SeedAccordion(AppTempDb temp)
    {
        var lib = new LibraryRepository(temp.Db);
        var watch = new WatchRepository(temp.Db);
        var srcId = lib.UpsertSource(@"C:\vids", "V");
        var secId = lib.UpsertSection(srcId, "Creator A");

        // Multi-episode series (isStandalone=false) with 2 episodes.
        var multiSeriesId = lib.UpsertSeries(secId, "Multi Show", false);
        lib.UpsertVideo(multiSeriesId, @"C:\vids\Multi Show\e01.mkv", 1, "mkv");
        lib.UpsertVideo(multiSeriesId, @"C:\vids\Multi Show\e02.mkv", 2, "mkv");

        // Standalone series (isStandalone=true) with 1 episode.
        var standaloneSeriesId = lib.UpsertSeries(secId, "Standalone Film", true);
        lib.UpsertVideo(standaloneSeriesId, @"C:\vids\Standalone Film.mkv", 1, "mkv");

        var summaries = lib.GetSeriesSummaries(secId);
        var multi = summaries.Single(s => s.SeriesId == multiSeriesId);
        var solo = summaries.Single(s => s.SeriesId == standaloneSeriesId);
        return (lib, watch, multi, solo);
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

    [Fact]
    public async Task Activate_multi_episode_expands_and_loads_episodes()
    {
        using var temp = new AppTempDb();
        var (lib, watch, multiSeries, _) = SeedAccordion(temp);
        var vm = new SeriesViewModel(multiSeries, lib, watch, new NullThumbs());

        await vm.ActivateCommand.ExecuteAsync(null);

        vm.IsExpanded.ShouldBeTrue();
        vm.Episodes.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Activate_multi_episode_second_call_collapses_without_reload()
    {
        using var temp = new AppTempDb();
        var (lib, watch, multiSeries, _) = SeedAccordion(temp);
        var vm = new SeriesViewModel(multiSeries, lib, watch, new NullThumbs());

        // First activate: expands + loads.
        await vm.ActivateCommand.ExecuteAsync(null);
        vm.IsExpanded.ShouldBeTrue();
        var episodeCountAfterFirstLoad = vm.Episodes.Count;
        episodeCountAfterFirstLoad.ShouldBeGreaterThan(0);

        // Second activate: collapses; episodes still loaded (no re-fetch clears them).
        await vm.ActivateCommand.ExecuteAsync(null);
        vm.IsExpanded.ShouldBeFalse();
        // Episodes were not reloaded (lazy flag prevents it), count unchanged.
        vm.Episodes.Count.ShouldBe(episodeCountAfterFirstLoad);
    }

    [Fact]
    public async Task Activate_standalone_raises_PlayRequested_and_does_not_expand()
    {
        using var temp = new AppTempDb();
        var (lib, watch, _, standalone) = SeedAccordion(temp);
        var vm = new SeriesViewModel(standalone, lib, watch, new NullThumbs());

        EpisodeView? played = null;
        vm.PlayRequested += (_, e) => played = e;

        await vm.ActivateCommand.ExecuteAsync(null);

        played.ShouldNotBeNull();
        vm.IsExpanded.ShouldBeFalse();
    }

    [Fact]
    public void EpisodeCountLabel_standalone_returns_Standalone()
    {
        using var temp = new AppTempDb();
        var (lib, watch, _, standalone) = SeedAccordion(temp);
        var vm = new SeriesViewModel(standalone, lib, watch, new NullThumbs());

        vm.EpisodeCountLabel.ShouldBe("Standalone");
    }

    [Fact]
    public void EpisodeCountLabel_multi_episode_returns_N_episodes()
    {
        using var temp = new AppTempDb();
        var (lib, watch, multiSeries, _) = SeedAccordion(temp);
        var vm = new SeriesViewModel(multiSeries, lib, watch, new NullThumbs());

        vm.EpisodeCountLabel.ShouldBe($"{multiSeries.EpisodeCount} episodes");
    }

}
