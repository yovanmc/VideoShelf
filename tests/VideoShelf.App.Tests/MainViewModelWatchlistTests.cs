using Shouldly;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using Xunit;

namespace VideoShelf.App.Tests;

public class MainViewModelWatchlistTests
{
    [Fact]
    public void ShowWatchlistCommand_sets_CurrentView_to_Watchlist_and_pushes_back_stack()
    {
        var vm = MainViewModelTestFactory.Create(out var ctx);
        using var _d = ctx.Db;

        vm.ShowWatchlistCommand.Execute(null);

        vm.CurrentView.ShouldBe(AppView.Watchlist);
        vm.CanGoBack.ShouldBeTrue();
    }

    [Fact]
    public void ShowWatchlistCommand_then_GoBack_returns_to_prior_view()
    {
        var vm = MainViewModelTestFactory.Create(out var ctx);
        using var _d = ctx.Db;

        vm.CurrentView.ShouldBe(AppView.Home);
        vm.ShowWatchlistCommand.Execute(null);
        vm.CurrentView.ShouldBe(AppView.Watchlist);

        vm.GoBackCommand.Execute(null);
        vm.CurrentView.ShouldBe(AppView.Home);
        vm.CanGoBack.ShouldBeFalse();
    }
}
