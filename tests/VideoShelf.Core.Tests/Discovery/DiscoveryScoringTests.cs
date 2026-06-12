using Shouldly;
using VideoShelf.Core.Discovery;
using Xunit;

namespace VideoShelf.Core.Tests.Discovery;

public sealed class DiscoveryScoringTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RecencyWeight_is_1_at_zero_age_and_half_at_one_halflife()
    {
        DiscoveryScoring.RecencyWeight(Now, Now, halfLifeDays: 14).ShouldBe(1.0, 1e-9);
        DiscoveryScoring.RecencyWeight(Now.AddDays(-14), Now, 14).ShouldBe(0.5, 1e-9);
        DiscoveryScoring.RecencyWeight(Now.AddDays(-28), Now, 14).ShouldBe(0.25, 1e-9);
    }

    [Fact]
    public void RecencyWeight_clamps_future_events_to_1()
    {
        DiscoveryScoring.RecencyWeight(Now.AddDays(5), Now, 14).ShouldBe(1.0, 1e-9);
    }

    [Fact]
    public void BuildTagAffinity_accumulates_recency_weighted_per_tag()
    {
        var events = new[]
        {
            new WatchedTag("comedy", Now),               // weight 1.0
            new WatchedTag("comedy", Now.AddDays(-14)),  // weight 0.5
            new WatchedTag("drama", Now.AddDays(-14)),   // weight 0.5
        };
        var aff = DiscoveryScoring.BuildTagAffinity(events, Now, halfLifeDays: 14);
        aff["comedy"].ShouldBe(1.5, 1e-9);
        aff["drama"].ShouldBe(0.5, 1e-9);
    }

    [Fact]
    public void ScoreSection_zero_when_no_tag_overlap()
    {
        var aff = new Dictionary<string, double> { ["comedy"] = 2.0 };
        DiscoveryScoring.ScoreSection(new[] { "horror" }, aff, unwatchedCount: 5, episodeCount: 5)
            .ShouldBe(0.0, 1e-9);
    }

    [Fact]
    public void ScoreSection_weights_overlap_by_unwatched_ratio()
    {
        var aff = new Dictionary<string, double> { ["comedy"] = 2.0 };
        DiscoveryScoring.ScoreSection(new[] { "comedy" }, aff, 10, 10).ShouldBe(2.0, 1e-9);
        DiscoveryScoring.ScoreSection(new[] { "comedy" }, aff, 0, 10).ShouldBe(1.0, 1e-9);
    }

    [Fact]
    public void ScoreSection_sums_multiple_overlapping_tags()
    {
        var aff = new Dictionary<string, double> { ["comedy"] = 2.0, ["drama"] = 1.0 };
        DiscoveryScoring.ScoreSection(new[] { "comedy", "drama" }, aff, 5, 10).ShouldBe(2.25, 1e-9);
    }
}
