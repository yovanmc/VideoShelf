using VideoShelf.App.Harness;

namespace VideoShelf.App.Tests.Harness;

public class HarnessStressOptionsTests
{
    [Fact]
    public void Parses_stress_and_metrics_out()
    {
        var o = HarnessOptions.Parse(new[]
            { "--stress", "500x200x5000", "--metrics-out", @"C:\tmp\metrics.json", "--view", "Browse", "--done-signal", @"C:\tmp\done" });
        Assert.Equal("500x200x5000", o.StressSpec);
        Assert.Equal(@"C:\tmp\metrics.json", o.MetricsOut);
        Assert.True(o.IsHarness);
    }

    [Fact]
    public void ParseStressSpec_returns_correct_tuple()
    {
        var o = HarnessOptions.Parse(new[] { "--stress", "500x200x5000" });
        var (creators, biggest, total) = o.ParseStressSpec();
        Assert.Equal(500, creators);
        Assert.Equal(200, biggest);
        Assert.Equal(5000, total);
    }

    [Fact]
    public void IsHarness_true_when_only_stress_set()
    {
        var o = HarnessOptions.Parse(new[] { "--stress", "10x5x50" });
        Assert.True(o.IsHarness);
    }

    [Fact]
    public void IsHarness_true_when_only_metrics_out_set()
    {
        var o = HarnessOptions.Parse(new[] { "--metrics-out", @"C:\m.json" });
        Assert.True(o.IsHarness);
    }
}
