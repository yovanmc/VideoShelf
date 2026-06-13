using Shouldly;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using Xunit;

namespace VideoShelf.App.Tests;

public class MainViewModelFavoritesTests
{
    [Fact]
    public void ShowFavoritesCommand_sets_CurrentView_to_Favorites_and_pushes_back_stack()
    {
        var vm = MainViewModelTestFactory.Create(out var ctx);
        using var _d = ctx.Db;

        vm.ShowFavoritesCommand.Execute(null);

        vm.CurrentView.ShouldBe(AppView.Favorites);
        vm.CanGoBack.ShouldBeTrue();
    }

    [Fact]
    public void ShowFavoritesCommand_then_GoBack_returns_to_prior_view()
    {
        var vm = MainViewModelTestFactory.Create(out var ctx);
        using var _d = ctx.Db;

        vm.CurrentView.ShouldBe(AppView.Home);
        vm.ShowFavoritesCommand.Execute(null);
        vm.CurrentView.ShouldBe(AppView.Favorites);

        vm.GoBackCommand.Execute(null);
        vm.CurrentView.ShouldBe(AppView.Home);
        vm.CanGoBack.ShouldBeFalse();
    }
}
