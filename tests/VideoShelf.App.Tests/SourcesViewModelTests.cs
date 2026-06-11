using System.Linq;
using Shouldly;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.Tests;

public class SourcesViewModelTests
{
    [Fact]
    public void AddSource_picks_a_folder_and_persists_it()
    {
        using var temp = new AppTempDb();
        var lib = new LibraryRepository(temp.Db);
        var picker = new FakeFolderPicker(@"C:\Videos\RootA");
        var vm = new SourcesViewModel(lib, picker);
        vm.Load();

        vm.AddSourceCommand.Execute(null);

        vm.Sources.Select(s => s.RootPath).ShouldBe(new[] { @"C:\Videos\RootA" });
        lib.GetSources().Single().RootPath.ShouldBe(@"C:\Videos\RootA");
    }

    [Fact]
    public void AddSource_cancelled_picker_adds_nothing()
    {
        using var temp = new AppTempDb();
        var lib = new LibraryRepository(temp.Db);
        var vm = new SourcesViewModel(lib, new FakeFolderPicker((string?)null));
        vm.Load();

        vm.AddSourceCommand.Execute(null);

        vm.Sources.ShouldBeEmpty();
    }

    [Fact]
    public void RemoveSource_deletes_the_selected_source()
    {
        using var temp = new AppTempDb();
        var lib = new LibraryRepository(temp.Db);
        var vm = new SourcesViewModel(lib, new FakeFolderPicker(@"C:\Videos\RootA"));
        vm.Load();
        vm.AddSourceCommand.Execute(null);
        var added = vm.Sources.Single();

        vm.RemoveSourceCommand.Execute(added);

        vm.Sources.ShouldBeEmpty();
        lib.GetSources().ShouldBeEmpty();
    }
}
