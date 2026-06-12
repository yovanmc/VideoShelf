using System.Threading.Tasks;
using Shouldly;
using VideoShelf.App.ViewModels;
using VideoShelf.App.Tests.TestSupport;
using Xunit;

namespace VideoShelf.App.Tests;

public sealed class MainViewModelNavigationTests
{
    [Fact]
    public void Default_view_is_home()
    {
        var vm = MainViewModelTestFactory.Create(out _);
        vm.CurrentView.ShouldBe(AppView.Home);
    }

    [Fact]
    public void ShowBrowse_then_ShowHome_switches_view()
    {
        var vm = MainViewModelTestFactory.Create(out _);
        vm.ShowBrowseCommand.Execute(null);
        vm.CurrentView.ShouldBe(AppView.Browse);
        vm.ShowHomeCommand.Execute(null);
        vm.CurrentView.ShouldBe(AppView.Home);
    }

    [Fact]
    public async Task OpenSection_switches_to_section_detail()
    {
        var vm = MainViewModelTestFactory.Create(out var ctx);
        using var _ = ctx.Db;
        await vm.OpenSectionAsync(ctx.SectionId);
        vm.CurrentView.ShouldBe(AppView.SectionDetail);
        vm.SectionDetail.SectionId.ShouldBe(ctx.SectionId);
    }
}
