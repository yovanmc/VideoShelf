using System.Threading;
using System.Threading.Tasks;
using VideoShelf.App.Services;
using VideoShelf.App.ViewModels;
using VideoShelf.App.ViewModels.Discovery;
using VideoShelf.Core.Discovery;
using VideoShelf.Core.Storage;

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
        var player = new PlayerViewModel(engine, lib, watch, settings, new ResumePolicy());
        var settingsVm = new SettingsViewModel(settings);
        var discoveryVm = new DiscoveryViewModel(disc, lib, tags);
        var sectionDetailVm = new SectionDetailViewModel(lib, tags, watch, thumbs);

        var vm = new MainViewModel(sources, libraryVm, new NullScan(), player, settingsVm,
            discoveryVm, sectionDetailVm);

        ctx = new MainVmContext(temp, sectionId);
        return vm;
    }
}
