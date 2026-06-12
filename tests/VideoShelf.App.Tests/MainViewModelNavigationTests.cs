using System.Threading.Tasks;
using Shouldly;
using VideoShelf.App.ViewModels;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.Core.Models;
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

    [Fact]
    public void Typing_in_Search_Query_flips_CurrentView_to_Search()
    {
        var vm = MainViewModelTestFactory.Create(out _);
        vm.Search.Query = "x";
        vm.CurrentView.ShouldBe(AppView.Search);
    }

    [Fact]
    public async Task Search_PlayRequested_routes_to_player()
    {
        // The ctor wires Search.PlayRequested -> PlayEpisode which sets IsPlayerVisible.
        // Seed a video under "TestSeries", search for it, click the video card's Play command —
        // the wired handler must open the player.
        var vm = MainViewModelTestFactory.Create(out var ctx);
        using var _ = ctx.Db;

        vm.Search.Query = "TestSeries";
        await vm.Search.WaitForIdleAsync();
        vm.Search.VideoResults.Count.ShouldBeGreaterThanOrEqualTo(1);

        vm.Search.VideoResults[0].PlayCommand.Execute(null);

        vm.IsPlayerVisible.ShouldBeTrue();
    }

    [Fact]
    public async Task Search_OpenCreatorRequested_opens_section()
    {
        // The ctor wires Search.OpenCreatorRequested -> OpenSectionAsync.
        // Seed "TestSection", search for "Test" so a creator card appears, then
        // invoke its Open command — the wired handler must flip CurrentView to SectionDetail.
        var vm = MainViewModelTestFactory.Create(out var ctx);
        using var _ = ctx.Db;

        vm.Search.Query = "TestSection";
        await vm.Search.WaitForIdleAsync();
        vm.Search.CreatorResults.Count.ShouldBeGreaterThanOrEqualTo(1);

        // Fire the card's Open command — this raises SearchViewModel.OpenCreatorRequested,
        // which the ctor-wired handler converts to OpenSectionAsync.
        vm.Search.CreatorResults[0].OpenCommand.Execute(null);
        // Give the async handler a tick to complete.
        await Task.Yield();

        vm.CurrentView.ShouldBe(AppView.SectionDetail);
        vm.SectionDetail.SectionId.ShouldBe(ctx.SectionId);
    }
}
