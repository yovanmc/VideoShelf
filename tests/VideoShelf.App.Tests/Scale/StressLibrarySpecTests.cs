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

    [Fact]
    public void Creator_names_are_person_names_unique_and_stable_across_calls()
    {
        // 500 creators — all within the 625-combo pool, so every name is unique and contains no "Creator NNNN" pattern.
        var spec = StressLibrarySpec.Generate(creators: 500, biggestSeries: 5, totalVideos: 500, seed: 42);

        var names = spec.Creators.Select(c => c.Name).ToList();

        // All names must be unique.
        Assert.Equal(names.Count, names.Distinct().Count());

        // Names must look like person names (contain a space), not the old "Creator NNNN" format.
        Assert.All(names, n => Assert.Contains(' ', n));
        Assert.DoesNotContain(names, n => n.StartsWith("Creator ", StringComparison.Ordinal) && n.Length == 12);

        // Stable across two Generate calls with the same seed.
        var spec2 = StressLibrarySpec.Generate(500, 5, 500, seed: 42);
        Assert.Equal(names, spec2.Creators.Select(c => c.Name).ToList());
    }
}
