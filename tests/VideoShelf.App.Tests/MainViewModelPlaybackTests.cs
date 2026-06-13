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


public class MainViewModelPlaybackTests
{
    private sealed class NullThumbs : IThumbnailService
    {
        public System.Threading.Tasks.Task<string?> GetThumbnailPathAsync(string videoPath, System.Threading.CancellationToken ct)
            => System.Threading.Tasks.Task.FromResult<string?>(null);
    }

    private sealed class NullScan : IScanCoordinator
    {
        public bool IsBusy => false;
        public System.Threading.Tasks.Task ScanAllAsync(System.Threading.CancellationToken ct)
            => System.Threading.Tasks.Task.CompletedTask;
    }

    private static MainViewModel Make(AppTempDb temp, FakePlaybackEngine engine, out long videoId)
    {
        var lib = new LibraryRepository(temp.Db);
        var watch = new WatchRepository(temp.Db);
        var tags = new TagRepository(temp.Db);
        var settings = new SettingsRepository(temp.Db);
        var seriesId = lib.UpsertSeries(lib.UpsertSection(lib.UpsertSource(@"C:\V", "V"), "S"), "Base", false);
        videoId = lib.UpsertVideo(seriesId, @"C:\V\S\a.mp4", 1, ".mp4");
        var thumbs = new NullThumbs();
        var library = new LibraryViewModel(lib, watch, thumbs);
        var sources = new SourcesViewModel(lib, new FakeFolderPicker());
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
        var searchCardFactory = new CreatorCardFactory(art, thumbs);
        var searchVm = new SearchViewModel(lib, searchCardFactory);
        var smartViewsVm = new SmartViewsViewModel(smartViews, tags, lib);
        var curation = new CurationRepository(temp.Db);
        var favoritesVm = new FavoritesViewModel(curation, lib);
        var watchlistVm = new WatchlistViewModel(curation, lib);
        var playlistsVm = new PlaylistsViewModel(new PlaylistRepository(temp.Db), playQueue);
        var historyVm = new HistoryViewModel(new HistoryRepository(temp.Db), lib);
        return new MainViewModel(sources, library, new NullScan(), player, settingsVm,
            discoveryVm, sectionDetailVm, renameTool, creators, searchVm,
            new MediaBackfillService(lib, new FakeMediaProbe()), playQueue, smartViewsVm, favoritesVm, watchlistVm,
            playlistsVm, historyVm, lib);
    }

    [Fact]
    public void Playing_an_episode_opens_the_player_and_shows_player_pane()
    {
        using var temp = new AppTempDb();
        var engine = new FakePlaybackEngine();
        var vm = Make(temp, engine, out var videoId);
        var ep = new EpisodeView(videoId, 1, @"C:\V\S\a.mp4", 1, "Base", false, false);

        vm.PlayEpisode(ep);

        vm.IsPlayerVisible.ShouldBeTrue();
        // missing path → PlaybackError set, engine not loaded; the routing itself is what we assert:
        vm.Player.Title.ShouldBe("Base");
    }

    [Fact]
    public void TogglePiP_flips_IsPictureInPicture()
    {
        using var temp = new AppTempDb();
        var engine = new FakePlaybackEngine();
        var vm = Make(temp, engine, out _);

        vm.TogglePictureInPictureCommand.Execute(null);

        vm.IsPictureInPicture.ShouldBeTrue();
    }

    [Fact]
    public void PlaybackEnded_via_queue_auto_advance_reopens_next_episode()
    {
        using var temp = new AppTempDb();
        var engine = new FakePlaybackEngine();
        var lib = new LibraryRepository(temp.Db);
        var watch = new WatchRepository(temp.Db);
        var settings = new SettingsRepository(temp.Db);
        settings.SetAutoAdvanceEpisodes(true);
        var seriesId = lib.UpsertSeries(lib.UpsertSection(lib.UpsertSource(@"C:\V", "V"), "S"), "Base", false);
        lib.UpsertVideo(seriesId, @"C:\V\S\a.mp4", 1, ".mp4");
        lib.UpsertVideo(seriesId, @"C:\V\S\b.mp4", 2, ".mp4");
        var ep1 = lib.GetEpisodes(seriesId)[0];
        var ep2 = lib.GetEpisodes(seriesId)[1];

        var thumbs = new NullThumbs();
        var sources = new SourcesViewModel(lib, new FakeFolderPicker());
        var libraryVm = new LibraryViewModel(lib, watch, thumbs);
        var player = new PlayerViewModel(engine, lib, watch, settings, new ResumePolicy(), new FakeSubtitleFilePicker());
        var settingsVm = new SettingsViewModel(settings);
        var disc = new DiscoveryRepository(temp.Db, lib, new TagRepository(temp.Db));
        var art = new CreatorArtRepository(temp.Db);
        var cardFactory = new CreatorCardFactory(art, thumbs);
        var statsRepo = new StatsRepository(temp.Db);
        var tags = new TagRepository(temp.Db);
        var playQueue = new PlayQueueViewModel(lib, settings);
        var smartViews2 = new SmartViewRepository(temp.Db);
        var discoveryVm = new DiscoveryViewModel(disc, lib, tags, cardFactory, statsRepo, playQueue, smartViews2);
        var sectionDetailVm = new SectionDetailViewModel(lib, tags, watch, thumbs, art, new FakeImagePicker(null), playQueue);
        var fs = new InMemoryFileSystem();
        var paths = new AppPaths(temp.DbPath + "-dir");
        var renameTool = new RenameToolViewModel(lib, new RenamePlanner(fs), new RenameExecutor(fs, lib), settings, paths);
        var creators = new CreatorsViewModel(lib, art, thumbs);
        var searchVm = new SearchViewModel(lib, new CreatorCardFactory(art, thumbs));
        var smartViewsVm2 = new SmartViewsViewModel(smartViews2, tags, lib);
        var curation2 = new CurationRepository(temp.Db);
        var favoritesVm2 = new FavoritesViewModel(curation2, lib);
        var watchlistVm2 = new WatchlistViewModel(curation2, lib);
        var playlistsVm2 = new PlaylistsViewModel(new PlaylistRepository(temp.Db), playQueue);
        var historyVm2 = new HistoryViewModel(new HistoryRepository(temp.Db), lib);
        var vm = new MainViewModel(sources, libraryVm, new NullScan(), player, settingsVm,
            discoveryVm, sectionDetailVm, renameTool, creators, searchVm,
            new MediaBackfillService(lib, new FakeMediaProbe()), playQueue, smartViewsVm2, favoritesVm2, watchlistVm2,
            playlistsVm2, historyVm2, lib);

        vm.PlayEpisode(ep1);
        vm.Player.RaisePlaybackEndedForTest(ep1);

        vm.Player.Title.ShouldBe(ep2.Title);
    }
}
