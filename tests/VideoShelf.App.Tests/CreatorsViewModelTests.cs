using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Shouldly;
using VideoShelf.App.Services;
using VideoShelf.App.ViewModels;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.Core.Scanning;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;
using Xunit;

namespace VideoShelf.App.Tests;

public class CreatorsViewModelTests
{
    private sealed class NullThumbs : IThumbnailService
    {
        public Task<string?> GetThumbnailPathAsync(string videoPath, CancellationToken cancellationToken)
            => Task.FromResult<string?>(null);
    }

    [Fact]
    public async Task Loads_one_card_per_creator_with_counts_and_open_callback()
    {
        using var temp = new AppTempDb();
        using var dir = new TempDir();
        dir.Touch("Creator A/Story 1.mp4");
        dir.Touch("Creator A/Story 2.mp4");
        dir.Touch("Creator B/Clip.mp4");

        var lib = new LibraryRepository(temp.Db);
        var art = new CreatorArtRepository(temp.Db);
        new ScanService(temp.Db, lib).ScanSource(dir.Path, "V");

        var opened = new List<long>();
        var vm = new CreatorsViewModel(lib, art, new NullThumbs());
        vm.OpenCreatorRequested += id => opened.Add(id);

        await vm.LoadAsync(CancellationToken.None);

        vm.Creators.Select(c => c.Name).ShouldBe(new[] { "Creator A", "Creator B" });
        vm.Creators.Single(c => c.Name == "Creator A").VideoCountLabel.ShouldBe("2 videos");

        vm.Creators.First().OpenCommand.Execute(null);
        opened.ShouldContain(vm.Creators.First().SectionId);
    }
}
