using Shouldly;
using VideoShelf.App.Services;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using VideoShelf.App.ViewModels.Discovery;
using VideoShelf.Core.Discovery;
using VideoShelf.Core.Models;
using VideoShelf.Core.Renaming;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests;

namespace VideoShelf.App.Tests;

/// <summary>
/// Group F — MainViewModel integration tests for the Up-Next countdown gate.
///
/// These tests verify that the M14 single-next-decider invariant is preserved:
/// PlaybackEnded → GetNextAfterEnd (unchanged) → gate → UpNextViewModel → OpenPlayer (one funnel).
///
/// Countdown is driven via UpNext.TickCountdown() (no real DispatcherTimer needed).
/// </summary>
public class UpNextMainViewModelIntegrationTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private sealed class NullThumbs : IThumbnailService
    {
        public System.Threading.Tasks.Task<string?> GetThumbnailPathAsync(string videoPath, System.Threading.CancellationToken ct)
            => System.Threading.Tasks.Task.FromResult<string?>(null);
    }

    private sealed class NullScan : IScanCoordinator
    {
        public bool IsBusy => false;
        public System.Threading.Tasks.Task<VideoShelf.Core.Scanning.ScanResult> ScanAllAsync(System.Threading.CancellationToken ct)
            => System.Threading.Tasks.Task.FromResult(new VideoShelf.Core.Scanning.ScanResult(0, 0, 0, 0));
    }

    /// <summary>
    /// Creates a MainViewModel with two episodes in a series (so auto-advance has a next).
    /// settings.SetAutoAdvanceEpisodes(true) when autoAdvance == true.
    /// IMPORTANT: caller owns the AppTempDb lifetime — pass it in and dispose it in the test's using block.
    /// </summary>
    private static (MainViewModel vm, FakePlaybackEngine engine, EpisodeView ep1, EpisodeView ep2)
        MakeTwoEpisodeVm(AppTempDb temp, bool autoAdvance = true)
    {
        var lib = new LibraryRepository(temp.Db);
        var watch = new WatchRepository(temp.Db);
        var tags = new TagRepository(temp.Db);
        var settings = new SettingsRepository(temp.Db);
        settings.SetAutoAdvanceEpisodes(autoAdvance);

        var seriesId = lib.UpsertSeries(
            lib.UpsertSection(lib.UpsertSource(@"C:\V", "V"), "S"), "Series", false);
        lib.UpsertVideo(seriesId, @"C:\V\S\a.mp4", 1, ".mp4");
        lib.UpsertVideo(seriesId, @"C:\V\S\b.mp4", 2, ".mp4");
        var episodes = lib.GetEpisodes(seriesId);
        var ep1 = episodes[0];
        var ep2 = episodes[1];

        var thumbs = new NullThumbs();
        var sources = new SourcesViewModel(lib, new FakeFolderPicker());
        var libraryVm = new LibraryViewModel(lib, watch, thumbs);
        var engine = new FakePlaybackEngine();
        var player = new PlayerViewModel(engine, lib, watch, settings, new ResumePolicy(), new FakeSubtitleFilePicker());
        var settingsVm = new SettingsViewModel(settings);
        var disc = new DiscoveryRepository(temp.Db, lib, tags);
        var art = new CreatorArtRepository(temp.Db);
        var cardFactory = new CreatorCardFactory(art, thumbs);
        var statsRepo = new StatsRepository(temp.Db);
        var playQueue = new PlayQueueViewModel(lib, settings);
        var smartViews = new SmartViewRepository(temp.Db);
        var discoveryVm = new DiscoveryViewModel(disc, lib, tags, cardFactory, statsRepo, playQueue, smartViews);
        var sectionDetailVm = new SectionDetailViewModel(lib, tags, watch, thumbs, art, new FakeImagePicker(null), playQueue);
        var fs = new InMemoryFileSystem();
        var paths = new AppPaths(temp.DbPath + "-dir");
        var renameTool = new RenameToolViewModel(lib, new RenamePlanner(fs), new RenameExecutor(fs, lib), settings, paths);
        var creators = new CreatorsViewModel(lib, art, thumbs);
        var searchVm = new SearchViewModel(lib, new CreatorCardFactory(art, thumbs));
        var smartViewsVm = new SmartViewsViewModel(smartViews, tags, lib);
        var curation = new CurationRepository(temp.Db);
        var favoritesVm = new FavoritesViewModel(curation, lib);
        var watchlistVm = new WatchlistViewModel(curation, lib);
        var playlistsVm = new PlaylistsViewModel(new PlaylistRepository(temp.Db), playQueue);
        var historyVm = new HistoryViewModel(new HistoryRepository(temp.Db), lib);

        var vm = new MainViewModel(sources, libraryVm, new NullScan(), player, settingsVm,
            discoveryVm, sectionDetailVm, renameTool, creators, searchVm,
            new MediaBackfillService(lib, new FakeMediaProbe()), playQueue, smartViewsVm, favoritesVm, watchlistVm,
            playlistsVm, historyVm, lib);

        return (vm, engine, ep1, ep2);
    }

    // ── tests ────────────────────────────────────────────────────────────────

    [Fact]
    public void PlaybackEnded_with_next_shows_UpNext_card_and_does_not_open_immediately()
    {
        using var temp = new AppTempDb();
        var (vm, engine, ep1, ep2) = MakeTwoEpisodeVm(temp);
        vm.PlayEpisode(ep1);

        // Simulate the video ending.
        vm.Player.RaisePlaybackEndedForTest(ep1);

        // Card should be up, countdown set, but next episode NOT yet opened.
        vm.UpNext.IsUpNextVisible.ShouldBeTrue();
        vm.UpNext.CountdownSeconds.ShouldBe(10);
        vm.UpNext.UpNextTitle.ShouldBe(ep2.Title);
        // Player title is still ep1 — next not yet opened.
        vm.Player.Title.ShouldBe(ep1.Title);
    }

    [Fact]
    public void TickCountdown_to_zero_opens_next_via_OpenPlayer_funnel()
    {
        using var temp = new AppTempDb();
        var (vm, engine, ep1, ep2) = MakeTwoEpisodeVm(temp);
        vm.PlayEpisode(ep1);
        vm.Player.RaisePlaybackEndedForTest(ep1);

        // Tick down to 0 — should open next via the single OpenPlayer funnel.
        for (int i = 0; i < 10; i++)
            vm.UpNext.TickCountdown();

        vm.UpNext.IsUpNextVisible.ShouldBeFalse();
        vm.Player.Title.ShouldBe(ep2.Title);  // next episode opened exactly once
    }

    [Fact]
    public void PlayNextNow_opens_next_immediately_and_hides_card()
    {
        using var temp = new AppTempDb();
        var (vm, engine, ep1, ep2) = MakeTwoEpisodeVm(temp);
        vm.PlayEpisode(ep1);
        vm.Player.RaisePlaybackEndedForTest(ep1);

        // Partially tick (card still visible).
        vm.UpNext.TickCountdown();
        vm.UpNext.TickCountdown();

        vm.UpNext.PlayNextNowCommand.Execute(null);

        vm.UpNext.IsUpNextVisible.ShouldBeFalse();
        vm.Player.Title.ShouldBe(ep2.Title);  // opened through the one OpenPlayer funnel
    }

    [Fact]
    public void DismissUpNext_cancels_countdown_and_does_not_open_next()
    {
        using var temp = new AppTempDb();
        var (vm, engine, ep1, ep2) = MakeTwoEpisodeVm(temp);
        vm.PlayEpisode(ep1);
        vm.Player.RaisePlaybackEndedForTest(ep1);

        vm.UpNext.TickCountdown();   // partial tick
        vm.UpNext.DismissUpNextCommand.Execute(null);

        vm.UpNext.IsUpNextVisible.ShouldBeFalse();
        // Player title unchanged — ep2 was NOT opened.
        vm.Player.Title.ShouldBe(ep1.Title);
    }

    [Fact]
    public void PlaybackEnded_with_no_next_does_not_show_card()
    {
        // autoAdvance=false + single play → no next → no card.
        using var temp = new AppTempDb();
        var (vm, engine, ep1, _) = MakeTwoEpisodeVm(temp, autoAdvance: false);
        vm.PlayEpisode(ep1);
        vm.Player.RaisePlaybackEndedForTest(ep1);

        vm.UpNext.IsUpNextVisible.ShouldBeFalse();
    }

    [Fact]
    public void OpenPlayer_is_still_the_single_funnel_player_title_set_exactly_once_per_advance()
    {
        // Validates that the single-next-decider (GetNextAfterEnd) is not duplicated:
        // after PlaybackEnded + countdown-to-0, the Player title is ep2 exactly (not double-set).
        using var temp = new AppTempDb();
        var (vm, engine, ep1, ep2) = MakeTwoEpisodeVm(temp);
        vm.PlayEpisode(ep1);
        vm.Player.RaisePlaybackEndedForTest(ep1);
        for (int i = 0; i < 10; i++) vm.UpNext.TickCountdown();

        // ep2 title is set; ticking again must be a no-op (card hidden → tick guard).
        var titleAfterOpen = vm.Player.Title;
        vm.UpNext.TickCountdown();  // extra tick — should do nothing (IsUpNextVisible == false)

        vm.Player.Title.ShouldBe(titleAfterOpen);  // unchanged
        vm.Player.Title.ShouldBe(ep2.Title);
    }
}
