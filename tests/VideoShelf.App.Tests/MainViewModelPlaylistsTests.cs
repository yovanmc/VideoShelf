using Shouldly;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using Xunit;

namespace VideoShelf.App.Tests;

public class MainViewModelPlaylistsTests
{
    [Fact]
    public void ShowPlaylistsCommand_sets_CurrentView_to_Playlists_and_pushes_back_stack()
    {
        var vm = MainViewModelTestFactory.Create(out var ctx);
        using var _d = ctx.Db;

        vm.ShowPlaylistsCommand.Execute(null);

        vm.CurrentView.ShouldBe(AppView.Playlists);
        vm.CanGoBack.ShouldBeTrue();
    }

    [Fact]
    public void ShowPlaylistsCommand_then_GoBack_returns_to_prior_view()
    {
        var vm = MainViewModelTestFactory.Create(out var ctx);
        using var _d = ctx.Db;

        vm.CurrentView.ShouldBe(AppView.Home);
        vm.ShowPlaylistsCommand.Execute(null);
        vm.CurrentView.ShouldBe(AppView.Playlists);

        vm.GoBackCommand.Execute(null);
        vm.CurrentView.ShouldBe(AppView.Home);
        vm.CanGoBack.ShouldBeFalse();
    }
}
