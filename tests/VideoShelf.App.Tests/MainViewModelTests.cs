using System.Linq;
using System.Threading.Tasks;
using Shouldly;
using VideoShelf.App.Services;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Scanning;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;
using System.Threading;

namespace VideoShelf.App.Tests;

public class MainViewModelTests
{
    private sealed class NullThumbs : IThumbnailService
    {
        public Task<string?> GetThumbnailPathAsync(string videoPath, CancellationToken ct)
            => Task.FromResult<string?>(null);
    }

    [Fact]
    public async Task Scan_then_initialize_populates_sources_and_library()
    {
        using var temp = new AppTempDb();
        using var dir = new TempDir();
        dir.Touch("Creator A/Cool Story.mp4");

        var lib = new LibraryRepository(temp.Db);
        var watch = new WatchRepository(temp.Db);
        var scanService = new ScanService(temp.Db, lib);
        var coordinator = new ScanCoordinator(lib, scanService);

        var sources = new SourcesViewModel(lib, new FakeFolderPicker(dir.Path));
        var libraryVm = new LibraryViewModel(lib, watch, new NullThumbs());
        var vm = new MainViewModel(sources, libraryVm, coordinator);

        // Add a source via the sources VM, then scan + reload through the shell.
        sources.Load();
        sources.AddSourceCommand.Execute(null);
        await vm.ScanAndReloadCommand.ExecuteAsync(null);

        vm.Sources.Sources.Single().RootPath.ShouldBe(dir.Path);
        vm.Library.Sections.Single().DisplayName.ShouldBe("Creator A");
    }
}
