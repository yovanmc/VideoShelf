using Shouldly;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Models;

namespace VideoShelf.App.Tests;

/// <summary>
/// Group F — UpNextViewModel state-machine tests.
///
/// All tests drive the countdown via direct TickCountdown() calls (no real DispatcherTimer,
/// no Application.Current required). The open-callback is a captured Action so we can assert
/// it was called exactly once (or not at all).
/// </summary>
public class UpNextViewModelTests
{
    // Minimal EpisodeView for use across tests (file need not exist for VM-only tests).
    private static EpisodeView MakeEpisode(long videoId = 1, string title = "Episode 1", int epNo = 1)
        => new(videoId, epNo, @"C:\fake\e.mp4", 1, title, false, false);

    [Fact]
    public void ShowUpNext_sets_visible_and_countdown_and_does_not_open_next()
    {
        var vm = new UpNextViewModel();
        int openCallCount = 0;
        var ep = MakeEpisode(title: "Next Episode");

        vm.ShowUpNext(ep, () => openCallCount++);

        vm.IsUpNextVisible.ShouldBeTrue();
        vm.CountdownSeconds.ShouldBe(10);
        vm.UpNextTitle.ShouldBe("Next Episode");
        openCallCount.ShouldBe(0);   // NOT opened yet — countdown is the gate
    }

    [Fact]
    public void ShowUpNext_thumbnail_is_null_placeholder_limitation()
    {
        // Asserts the known limitation: thumbnail is always null because
        // RecencyCardViewModel.ThumbnailPath is never populated for video items.
        var vm = new UpNextViewModel();
        vm.ShowUpNext(MakeEpisode(), () => { });
        vm.UpNextThumbnailPath.ShouldBeNull();
    }

    [Fact]
    public void TickCountdown_decrements_and_does_not_open_before_zero()
    {
        var vm = new UpNextViewModel();
        int openCallCount = 0;
        vm.ShowUpNext(MakeEpisode(), () => openCallCount++);

        vm.TickCountdown();   // 10 → 9
        vm.TickCountdown();   // 9  → 8

        vm.CountdownSeconds.ShouldBe(8);
        openCallCount.ShouldBe(0);
        vm.IsUpNextVisible.ShouldBeTrue();
    }

    [Fact]
    public void TickCountdown_to_zero_opens_next_exactly_once()
    {
        var vm = new UpNextViewModel();
        int openCallCount = 0;
        vm.ShowUpNext(MakeEpisode(), () => openCallCount++);

        // Tick down from 10 to 0.
        for (int i = 0; i < 10; i++)
            vm.TickCountdown();

        openCallCount.ShouldBe(1);         // opened exactly once
        vm.IsUpNextVisible.ShouldBeFalse();  // card hidden after open
    }

    [Fact]
    public void TickCountdown_beyond_zero_does_not_open_again()
    {
        // Guards against double-fire if somehow TickCountdown is called extra times.
        var vm = new UpNextViewModel();
        int openCallCount = 0;
        vm.ShowUpNext(MakeEpisode(), () => openCallCount++);

        for (int i = 0; i < 15; i++)   // 5 extra ticks past 0
            vm.TickCountdown();

        openCallCount.ShouldBe(1);   // still exactly once
    }

    [Fact]
    public void PlayNextNow_opens_immediately_and_hides_card()
    {
        var vm = new UpNextViewModel();
        int openCallCount = 0;
        vm.ShowUpNext(MakeEpisode(), () => openCallCount++);

        // Partially tick (card still visible, countdown at 8).
        vm.TickCountdown();
        vm.TickCountdown();

        vm.PlayNextNowCommand.Execute(null);

        openCallCount.ShouldBe(1);
        vm.IsUpNextVisible.ShouldBeFalse();
    }

    [Fact]
    public void DismissUpNext_cancels_and_does_not_open()
    {
        var vm = new UpNextViewModel();
        int openCallCount = 0;
        vm.ShowUpNext(MakeEpisode(), () => openCallCount++);

        vm.TickCountdown();  // 10 → 9
        vm.DismissUpNextCommand.Execute(null);

        openCallCount.ShouldBe(0);
        vm.IsUpNextVisible.ShouldBeFalse();
    }

    [Fact]
    public void DismissUpNext_then_tick_does_not_open()
    {
        // After dismiss, further ticks must be no-ops (IsUpNextVisible guard).
        var vm = new UpNextViewModel();
        int openCallCount = 0;
        vm.ShowUpNext(MakeEpisode(), () => openCallCount++);
        vm.DismissUpNextCommand.Execute(null);

        vm.TickCountdown();   // should be a no-op (IsUpNextVisible == false)
        vm.TickCountdown();

        openCallCount.ShouldBe(0);
    }

    [Fact]
    public void Second_ShowUpNext_replaces_first_countdown_without_opening_first()
    {
        // If a second episode ends while the card is still showing, the card resets.
        var vm = new UpNextViewModel();
        int firstCallCount = 0, secondCallCount = 0;
        vm.ShowUpNext(MakeEpisode(videoId: 1, title: "Ep1"), () => firstCallCount++);
        vm.TickCountdown();  // partial countdown

        var ep2 = MakeEpisode(videoId: 2, title: "Ep2");
        vm.ShowUpNext(ep2, () => secondCallCount++);

        vm.IsUpNextVisible.ShouldBeTrue();
        vm.UpNextTitle.ShouldBe("Ep2");
        vm.CountdownSeconds.ShouldBe(10);  // reset to 10
        firstCallCount.ShouldBe(0);         // first callback NOT called

        // Tick second one to zero.
        for (int i = 0; i < 10; i++)
            vm.TickCountdown();

        secondCallCount.ShouldBe(1);
        firstCallCount.ShouldBe(0);  // first still never called
    }
}
