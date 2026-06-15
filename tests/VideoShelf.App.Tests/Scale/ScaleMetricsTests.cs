using VideoShelf.App.Scale;

namespace VideoShelf.App.Tests.Scale;

public class ScaleMetricsTests
{
    [Fact]
    public void Serializes_round_trips_the_metric_fields()
    {
        var m = new ScaleMetrics
        {
            View = "Browse",
            CreatorCount = 500,
            RenderedNodeCount = 38,
            InitialRenderMs = 220,
            ManagedHeapBytes = 123_456_789,
            ScanProbeMs = null,
        };
        var json = ScaleMetrics.ToJson(new[] { m });
        var back = ScaleMetrics.FromJson(json);
        Assert.Single(back);
        Assert.Equal("Browse", back[0].View);
        Assert.Equal(38, back[0].RenderedNodeCount);
    }
}
