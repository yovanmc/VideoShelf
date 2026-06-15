using VideoShelf.App.Scale;

namespace VideoShelf.App.Tests.Scale;

public class StressLibrarySpecTests
{
    [Fact]
    public void Generates_requested_creator_and_video_totals_deterministically()
    {
        var spec = StressLibrarySpec.Generate(creators: 500, biggestSeries: 200, totalVideos: 5000, seed: 1234);

        Assert.Equal(500, spec.Creators.Count);
        Assert.Equal(5000, spec.Creators.Sum(c => c.Series.Sum(s => s.EpisodeCount)));
        Assert.Equal(200, spec.Creators.Max(c => c.Series.Count));   // the biggest creator has the target series count

        // Determinism: same seed → identical shape
        var spec2 = StressLibrarySpec.Generate(500, 200, 5000, seed: 1234);
        Assert.Equal(spec.Creators.Select(c => c.Name), spec2.Creators.Select(c => c.Name));
        Assert.Equal(spec.Creators[0].Series.Count, spec2.Creators[0].Series.Count);
    }

    [Fact]
    public void Every_creator_series_and_episode_has_a_stable_unique_name()
    {
        var spec = StressLibrarySpec.Generate(10, 5, 50, seed: 7);
        var names = spec.Creators.SelectMany(c => c.Series.SelectMany(s => s.Episodes)).Select(e => e.RelativePath);
        Assert.Equal(names.Count(), names.Distinct().Count());
    }
}
