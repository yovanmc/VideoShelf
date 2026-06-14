using System.Linq;
using Shouldly;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.Tests;

/// <summary>
/// E2 unit tests: Remove-source confirm gate + undo flow.
/// Uses hand-written fakes (no mocking library) matching the existing App.Tests style.
/// </summary>
public class SourcesViewModelUndoTests
{
    // ── helpers ──────────────────────────────────────────────────────────────────

    private static SourcesViewModel BuildVm(
        AppTempDb temp,
        out FakeConfirmService fakeConfirm,
        bool confirmResult = true,
        string? pickerFolder = null)
    {
        fakeConfirm = new FakeConfirmService { NextResult = confirmResult };
        var lib    = new LibraryRepository(temp.Db);
        var picker = new FakeFolderPicker(pickerFolder);
        return new SourcesViewModel(lib, picker, fakeConfirm);
    }

    // ── confirm gate tests ────────────────────────────────────────────────────────

    [Fact]
    public void RemoveSource_without_confirm_does_not_delete()
    {
        using var temp = new AppTempDb();
        var vm = BuildVm(temp, out _, confirmResult: false, pickerFolder: @"C:\Videos\A");
        vm.Load();
        vm.AddSourceCommand.Execute(null);
        var src = vm.Sources.Single();

        vm.RemoveSourceCommand.Execute(src);

        vm.Sources.ShouldNotBeEmpty();
        new LibraryRepository(temp.Db).GetSources().ShouldNotBeEmpty();
    }

    [Fact]
    public void RemoveSource_with_confirm_deletes_and_enables_undo()
    {
        using var temp = new AppTempDb();
        var vm = BuildVm(temp, out _, confirmResult: true, pickerFolder: @"C:\Videos\A");
        vm.Load();
        vm.AddSourceCommand.Execute(null);
        var src = vm.Sources.Single();

        vm.RemoveSourceCommand.Execute(src);

        vm.Sources.ShouldBeEmpty();
        vm.CanUndoRemove.ShouldBeTrue();
    }

    // ── undo flow test (the E2 acceptance test) ──────────────────────────────────

    [Fact]
    public void RemoveSource_then_Undo_readds_the_source()
    {
        // Arrange: a settings VM with a hand-written fake confirm (returns true) and a lib.
        using var temp = new AppTempDb();
        var lib    = new LibraryRepository(temp.Db);
        var confirm = new FakeConfirmService { NextResult = true };
        var vm = new SourcesViewModel(lib, new FakeFolderPicker(@"C:\Videos\Movies"), confirm);
        vm.Load();
        vm.AddSourceCommand.Execute(null);
        var added = vm.Sources.Single();

        // Track whether OnSourceRestored fires.
        bool restoredFired = false;
        vm.OnSourceRestored = () => restoredFired = true;

        // Act: remove then undo.
        vm.RemoveSourceCommand.Execute(added);
        vm.Sources.ShouldBeEmpty("source removed");
        vm.UndoRemoveCommand.Execute(null);

        // Assert: the repo's UpsertSource was called (source is back in the DB).
        lib.GetSources().Select(s => s.RootPath).ShouldBe(new[] { @"C:\Videos\Movies" });
        // And the observable list is refreshed.
        vm.Sources.Select(s => s.RootPath).ShouldBe(new[] { @"C:\Videos\Movies" });
        // CanUndoRemove resets after undo.
        vm.CanUndoRemove.ShouldBeFalse();
        // OnSourceRestored callback fired.
        restoredFired.ShouldBeTrue();
    }

    [Fact]
    public void UndoRemove_is_disabled_before_any_removal()
    {
        using var temp = new AppTempDb();
        var vm = BuildVm(temp, out _, pickerFolder: @"C:\Videos\A");
        vm.Load();

        vm.CanUndoRemove.ShouldBeFalse();
        vm.UndoRemoveCommand.CanExecute(null).ShouldBeFalse();
    }
}
