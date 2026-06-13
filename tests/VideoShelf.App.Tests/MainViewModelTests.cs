using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Shouldly;
using VideoShelf.App.Services;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using VideoShelf.App.ViewModels.Discovery;
using VideoShelf.Core.Discovery;
using VideoShelf.Core.Renaming;
using VideoShelf.Core.Scanning;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests;
using VideoShelf.Core.Tests.TestSupport;

namespace VideoShelf.App.Tests;

public class MainViewModelTests
{
    private sealed class NullThumbs : IThumbnailService
    {
        public Task<string?> GetThumbnailPathAsync(string videoPath, CancellationToken ct)
            => Task.FromResult<string?>(null);
    }

    [Fact]
    public async Task ScanAndReload_refreshes_Discovery_rails()
    {
        // Use the factory which wires up a real DiscoveryRepository against the temp DB.
        var vm = MainViewModelTestFactory.Create(out var ctx);
        using var _d = ctx.Db;

        // Baseline: initialize with no resume data — ContinueWatching should be empty.
        await vm.InitializeAsync();
        vm.Discovery.ContinueWatching.ShouldBeEmpty();

        // Seed a resume position directly into the DB AFTER InitializeAsync so it isn't
        // picked up yet.  The factory's UpsertVideo returns the video id indirectly via
        // the section, so we look it up through the lib.
        var lib = new LibraryRepository(ctx.Db.Db);
        var series = lib.GetSeriesForSection(ctx.SectionId);
        var videos = lib.GetVideosForSeries(series[0].Id);
        lib.SetResumePosition(videos[0].Id, 120.0);

        // ScanAndReload must now re-query Discovery and surface the resumable video.
        await vm.ScanAndReloadCommand.ExecuteAsync(null);
        vm.Discovery.HasContinueWatching.ShouldBeTrue();
        vm.Discovery.ContinueWatching.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Scan_then_initialize_populates_sources_and_library()
    {
        using var temp = new AppTempDb();
        using var dir = new TempDir();
        dir.Touch("Creator A/Cool Story.mp4");

        var lib = new LibraryRepository(temp.Db);
        var watch = new WatchRepository(temp.Db);
        var settings = new SettingsRepository(temp.Db);
        var scanService = new ScanService(temp.Db, lib);
        var coordinator = new ScanCoordinator(lib, scanService);

        var tags = new TagRepository(temp.Db);
        var disc = new DiscoveryRepository(temp.Db, lib, tags);
        var thumbs = new NullThumbs();
        var sources = new SourcesViewModel(lib, new FakeFolderPicker(dir.Path));
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
        var playlistsVm = new PlaylistsViewModel(new PlaylistRepository(temp.Db), playQueue);
        var vm = new MainViewModel(sources, libraryVm, coordinator, player, settingsVm,
            discoveryVm, sectionDetailVm, renameTool, creators, searchVm,
            new MediaBackfillService(lib, new FakeMediaProbe()), playQueue, smartViewsVm, favoritesVm, watchlistVm,
            playlistsVm);

        // Add a source via the sources VM, then scan + reload through the shell.
        sources.Load();
        sources.AddSourceCommand.Execute(null);
        await vm.ScanAndReloadCommand.ExecuteAsync(null);

        vm.Sources.Sources.Single().RootPath.ShouldBe(dir.Path);
        vm.Library.Sections.Single().DisplayName.ShouldBe("Creator A");
    }

    [Fact]
    public void ShowSmartViewsCommand_sets_CurrentView_to_SmartViews_and_pushes_back_stack()
    {
        var vm = MainViewModelTestFactory.Create(out var ctx);
        using var _d = ctx.Db;

        // Navigate somewhere first so there's something on the back stack.
        vm.ShowSmartViewsCommand.Execute(null);

        vm.CurrentView.ShouldBe(AppView.SmartViews);
        vm.CanGoBack.ShouldBeTrue();
    }

    [Fact]
    public void ShowSmartViewsCommand_GoBack_returns_to_previous_view()
    {
        var vm = MainViewModelTestFactory.Create(out var ctx);
        using var _d = ctx.Db;

        // Start on Home.
        vm.CurrentView.ShouldBe(AppView.Home);

        vm.ShowSmartViewsCommand.Execute(null);
        vm.CurrentView.ShouldBe(AppView.SmartViews);

        vm.GoBackCommand.Execute(null);
        vm.CurrentView.ShouldBe(AppView.Home);
        vm.CanGoBack.ShouldBeFalse();
    }
}
