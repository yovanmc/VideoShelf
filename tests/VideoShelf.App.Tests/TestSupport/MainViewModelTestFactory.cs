using System.Threading;
using System.Threading.Tasks;
using VideoShelf.App.Services;
using VideoShelf.App.ViewModels;
using VideoShelf.App.ViewModels.Discovery;
using VideoShelf.Core.Discovery;
using VideoShelf.Core.Renaming;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests;

namespace VideoShelf.App.Tests.TestSupport;


public sealed record MainVmContext(AppTempDb Db, long SectionId);

public static class MainViewModelTestFactory
{
    private sealed class NullThumbs : IThumbnailService
    {
        public Task<string?> GetThumbnailPathAsync(string videoPath, CancellationToken ct)
            => Task.FromResult<string?>(null);
    }

    private sealed class NullScan : IScanCoordinator
    {
        public bool IsBusy => false;
        public Task ScanAllAsync(CancellationToken ct) => Task.CompletedTask;
    }

    public static MainViewModel Create(out MainVmContext ctx)
    {
        var temp = new AppTempDb();
        var lib = new LibraryRepository(temp.Db);
        var watch = new WatchRepository(temp.Db);
        var tags = new TagRepository(temp.Db);
        var settings = new SettingsRepository(temp.Db);
        var disc = new DiscoveryRepository(temp.Db, lib, tags);

        // Seed one source + section + series + video so ForYou can populate.
        var srcId = lib.UpsertSource(@"C:\V", "V");
        var sectionId = lib.UpsertSection(srcId, "TestSection");
        var seriesId = lib.UpsertSeries(sectionId, "TestSeries", false);
        lib.UpsertVideo(seriesId, @"C:\V\TestSeries\e01.mp4", 1, ".mp4");

        var thumbs = new NullThumbs();
        var sources = new SourcesViewModel(lib, new FakeFolderPicker());
        var libraryVm = new LibraryViewModel(lib, watch, thumbs);
        var engine = new FakePlaybackEngine();
        var player = new PlayerViewModel(engine, lib, watch, settings, new ResumePolicy(), new FakeSubtitleFilePicker());
        var settingsVm = new SettingsViewModel(settings);
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
        var playlistRepo = new PlaylistRepository(temp.Db);
        var playlistsVm = new PlaylistsViewModel(playlistRepo, playQueue);

        var vm = new MainViewModel(sources, libraryVm, new NullScan(), player, settingsVm,
            discoveryVm, sectionDetailVm, renameTool, creators, searchVm,
            new MediaBackfillService(lib, new FakeMediaProbe()), playQueue, smartViewsVm, favoritesVm, watchlistVm,
            playlistsVm);

        ctx = new MainVmContext(temp, sectionId);
        return vm;
    }
}
