using System;
using Shouldly;
using VideoShelf.App.Services;

namespace VideoShelf.App.Tests;

/// <summary>
/// Pure unit tests for <see cref="SnapshotPositionHelper"/>.
/// No libVLC, no disk, no DI — the helpers are fully deterministic.
/// </summary>
public class SnapshotPositionHelperTests
{
    // ── Clamp(TimeSpan requested, TimeSpan? duration) ───────────────────────

    [Fact]
    public void Clamp_null_duration_returns_zero()
        => SnapshotPositionHelper.Clamp(TimeSpan.FromSeconds(5), duration: null)
            .ShouldBe(TimeSpan.Zero);

    [Fact]
    public void Clamp_zero_duration_returns_zero()
        => SnapshotPositionHelper.Clamp(TimeSpan.FromSeconds(5), TimeSpan.Zero)
            .ShouldBe(TimeSpan.Zero);

    [Fact]
    public void Clamp_negative_requested_returns_zero()
        => SnapshotPositionHelper.Clamp(TimeSpan.FromSeconds(-1), TimeSpan.FromSeconds(60))
            .ShouldBe(TimeSpan.Zero);

    [Fact]
    public void Clamp_requested_within_range_is_unchanged()
    {
        var pos = TimeSpan.FromSeconds(30);
        SnapshotPositionHelper.Clamp(pos, TimeSpan.FromSeconds(60))
            .ShouldBe(pos);
    }

    [Fact]
    public void Clamp_requested_beyond_duration_returns_duration()
    {
        var dur = TimeSpan.FromSeconds(60);
        SnapshotPositionHelper.Clamp(TimeSpan.FromSeconds(9999), dur)
            .ShouldBe(dur);
    }

    [Fact]
    public void Clamp_requested_equal_to_duration_is_unchanged()
    {
        var dur = TimeSpan.FromSeconds(120);
        SnapshotPositionHelper.Clamp(dur, dur).ShouldBe(dur);
    }

    // ── DefaultSeekMs(long durationMs) ──────────────────────────────────────

    [Fact]
    public void DefaultSeekMs_zero_duration_returns_zero()
        => SnapshotPositionHelper.DefaultSeekMs(0).ShouldBe(0L);

    [Fact]
    public void DefaultSeekMs_negative_duration_returns_zero()
        => SnapshotPositionHelper.DefaultSeekMs(-1000).ShouldBe(0L);

    [Fact]
    public void DefaultSeekMs_ten_percent_when_duration_small()
    {
        // 20 000 ms → 10% = 2 000 ms  (< 3 000 cap)
        SnapshotPositionHelper.DefaultSeekMs(20_000).ShouldBe(2_000L);
    }

    [Fact]
    public void DefaultSeekMs_capped_at_3000_for_long_videos()
    {
        // 120 000 ms → 10% = 12 000 ms  → capped to 3 000
        SnapshotPositionHelper.DefaultSeekMs(120_000).ShouldBe(3_000L);
    }

    [Fact]
    public void DefaultSeekMs_exactly_30000_hits_cap()
    {
        // 30 000 ms → 10% = 3 000 ms  → exactly at cap
        SnapshotPositionHelper.DefaultSeekMs(30_000).ShouldBe(3_000L);
    }
}
