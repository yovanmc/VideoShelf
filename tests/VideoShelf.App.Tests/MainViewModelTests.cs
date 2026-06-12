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
        var player = new PlayerViewModel(engine, lib, watch, settings, new ResumePolicy());
        var settingsVm = new SettingsViewModel(settings);
        var discoveryVm = new DiscoveryViewModel(disc, lib, tags);
        var sectionDetailVm = new SectionDetailViewModel(lib, tags, watch, thumbs);
        var fs = new InMemoryFileSystem();
        var paths = new AppPaths(temp.DbPath + "-dir");
        var renameTool = new RenameToolViewModel(lib, new RenamePlanner(fs), new RenameExecutor(fs, lib), settings, paths);
        var vm = new MainViewModel(sources, libraryVm, coordinator, player, settingsVm,
            discoveryVm, sectionDetailVm, renameTool);

        // Add a source via the sources VM, then scan + reload through the shell.
        sources.Load();
        sources.AddSourceCommand.Execute(null);
        await vm.ScanAndReloadCommand.ExecuteAsync(null);

        vm.Sources.Sources.Single().RootPath.ShouldBe(dir.Path);
        vm.Library.Sections.Single().DisplayName.ShouldBe("Creator A");
    }
}
