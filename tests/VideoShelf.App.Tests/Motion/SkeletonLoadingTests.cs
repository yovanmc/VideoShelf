// tests/VideoShelf.App.Tests/Motion/SkeletonLoadingTests.cs
// M21 Group C regression: AnimationsEnabled delegation + ScanStatusText progress.
using System.Threading.Tasks;
using Shouldly;
using VideoShelf.App.Motion;
using VideoShelf.App.Tests.TestSupport;
using Xunit;

namespace VideoShelf.App.Tests.Motion;

public class SkeletonLoadingTests
{
    // ── Tiny hand-fake implementing IMotionPolicy ─────────────────────────────

    private sealed class StubMotionPolicy(bool shouldAnimate) : IMotionPolicy
    {
        public bool ShouldAnimate => shouldAnimate;
    }

    // ── Test 2: AnimationsEnabled delegates to IMotionPolicy ─────────────────

    [Fact]
    public void AnimationsEnabled_is_false_when_policy_returns_false()
    {
        var vm = MainViewModelTestFactory.Create(out var ctx, motion: new StubMotionPolicy(false));
        using var _ = ctx.Db;

        vm.AnimationsEnabled.ShouldBeFalse();
    }

    [Fact]
    public void AnimationsEnabled_is_true_when_policy_returns_true()
    {
        var vm = MainViewModelTestFactory.Create(out var ctx, motion: new StubMotionPolicy(true));
        using var _ = ctx.Db;

        vm.AnimationsEnabled.ShouldBeTrue();
    }

    [Fact]
    public void AnimationsEnabled_defaults_to_true_when_no_policy_injected()
    {
        // Factory with no motion policy -> falls back to true (test-context default).
        var vm = MainViewModelTestFactory.Create(out var ctx);
        using var _ = ctx.Db;

        vm.AnimationsEnabled.ShouldBeTrue();
    }

    // ── Test 3: ScanStatusText is non-empty after ScanAndReload completes ─────

    [Fact]
    public async Task ScanStatusText_is_non_empty_after_ScanAndReload()
    {
        var vm = MainViewModelTestFactory.Create(out var ctx);
        using var _ = ctx.Db;

        vm.ScanStatusText.ShouldBeEmpty(); // starts blank

        await vm.ScanAndReloadCommand.ExecuteAsync(null);

        vm.ScanStatusText.ShouldNotBeEmpty();
    }
}
