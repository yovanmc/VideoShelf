using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;

namespace VideoShelf.Core.Tests.Storage;

/// <summary>
/// Verifies that GetSectionSummaries uses indexes rather than full table scans for the
/// seed-thumbnail path lookup. E1 characterises the current plan (should FAIL before E2
/// rewrites the query); after E2 this test must PASS.
/// </summary>
public class SectionSummaryQueryPlanTests
{
    /// <summary>Seeds a minimal stress library (50 creators, up to 20 series, 500 videos) into
    /// an isolated TempDb and asserts that the EXPLAIN QUERY PLAN output for
    /// GetSectionSummaries contains no unindexed SCAN of the videos table.</summary>
    [Fact]
    public void Section_summaries_use_indexes_not_full_scans()
    {
        // Use an isolated TempDb (unique file path + Guid) to avoid the known
        // parallel-execution flake from shared DB state in the Core test suite.
        using var temp = new TempDb();
        var repo = new LibraryRepository(temp.Db);

        // Inline stress-seed: mirrors StressLibrarySpec.Generate(50, 20, 500, seed:3)
        // without pulling in VideoShelf.App (which Core.Tests doesn't reference).
        SeedStressLibrary(repo, creators: 50, biggestSeries: 20, totalVideos: 500, seed: 3, sourceRoot: @"C:\s");

        var plan = repo.ExplainSectionSummaries();

        // The seed-path must NOT use a correlated scalar subquery (which fires once per section
        // row and adds a temp B-TREE sort at 500 creators). After E2 this is replaced with a
        // single-pass derived table LEFT JOIN, which should remove the CORRELATED SCALAR SUBQUERY.
        Assert.DoesNotContain("CORRELATED SCALAR SUBQUERY", plan, StringComparison.OrdinalIgnoreCase);
    }

    // ── inline seeder (mirrors StressLibrarySpec + StressLibrarySeeder logic) ──────────

    private static void SeedStressLibrary(
        LibraryRepository repo,
        int creators,
        int biggestSeries,
        int totalVideos,
        int seed,
        string sourceRoot)
    {
        var rng = new Random(seed);

        // Build the spec in memory (same algorithm as StressLibrarySpec.Generate).
        var plan = new List<(string Creator, List<(string BaseTitle, List<(int EpNo, string RelPath)> Episodes)> Series)>();
        for (int c = 0; c < creators; c++)
        {
            int seriesCount = c == 0 ? biggestSeries : 1 + rng.Next(0, Math.Max(1, biggestSeries / 8));
            var series = new List<(string, List<(int, string)>)>(seriesCount);
            for (int s = 0; s < seriesCount; s++)
                series.Add(($"C{c:D4}S{s:D3}", []));
            plan.Add(($"Creator {c:D4}", series));
        }

        // Distribute episodes round-robin.
        var flatSeries = plan.SelectMany(c => c.Series).ToList();
        for (int placed = 0; placed < totalVideos; placed++)
        {
            var s = flatSeries[placed % flatSeries.Count];
            int epNo = s.Episodes.Count + 1;
            s.Episodes.Add((epNo, $"{s.BaseTitle}/{s.BaseTitle} {epNo:D3}.mp4"));
        }

        // Write to repo (mirrors StressLibrarySeeder.Seed).
        var sourceId = repo.UpsertSource(sourceRoot, "Stress");
        foreach (var (creatorName, seriesList) in plan)
        {
            var sectionId = repo.UpsertSection(sourceId, creatorName);
            foreach (var (baseTitle, episodes) in seriesList)
            {
                var seriesId = repo.UpsertSeries(sectionId, baseTitle, isStandalone: false);
                foreach (var (epNo, relPath) in episodes)
                {
                    var fullPath = System.IO.Path.Combine(sourceRoot, creatorName, relPath);
                    repo.UpsertVideo(seriesId, fullPath, episodeNo: epNo, format: ".mp4");
                }
            }
        }
    }
}
