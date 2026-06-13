using Shouldly;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using Xunit;

namespace VideoShelf.App.Tests;

public sealed class MainViewModelHistoryTests
{
    [Fact]
    public void ShowHistoryCommand_sets_CurrentView_to_History_and_pushes_back_stack()
    {
        var vm = MainViewModelTestFactory.Create(out var ctx);
        using var _d = ctx.Db;

        vm.ShowHistoryCommand.Execute(null);

        vm.CurrentView.ShouldBe(AppView.History);
        vm.CanGoBack.ShouldBeTrue();
    }

    [Fact]
    public void ShowHistoryCommand_then_GoBack_returns_to_prior_view()
    {
        var vm = MainViewModelTestFactory.Create(out var ctx);
        using var _d = ctx.Db;

        vm.CurrentView.ShouldBe(AppView.Home);
        vm.ShowHistoryCommand.Execute(null);
        vm.CurrentView.ShouldBe(AppView.History);

        vm.GoBackCommand.Execute(null);
        vm.CurrentView.ShouldBe(AppView.Home);
        vm.CanGoBack.ShouldBeFalse();
    }

    [Fact]
    public void History_property_exposed_on_MainViewModel()
    {
        var vm = MainViewModelTestFactory.Create(out var ctx);
        using var _d = ctx.Db;

        vm.History.ShouldNotBeNull();
    }
}
