using System.Linq;
using Shouldly;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;
using Xunit;

namespace VideoShelf.Core.Tests.Storage;

/// <summary>
/// M18-H: Tests for <see cref="LibraryRepository.RegroupSection"/>.
/// Verifies the no-disk-scan regroup correctly reflects grouping overrides.
/// </summary>
public sealed class RegroupSectionTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (TempDb Db, LibraryRepository Repo, long SectionId) NewFx()
    {
        var db   = new TempDb();
        var repo = new LibraryRepository(db.Db);
        var src  = repo.UpsertSource(@"C:\V", "V");
        var sec  = repo.UpsertSection(src, "Creator");
        return (db, repo, sec);
    }

    // ── RegroupSection: split ─────────────────────────────────────────────────

    [Fact]
    public void RegroupSection_Split_MovesVideoToNewSeries()
    {
        var (db, repo, sec) = NewFx(); using var _ = db;

        // Set up two episodes that naturally group under "Show".
        var s1 = repo.UpsertSeries(sec, "Show", false);
        repo.UpsertVideo(s1, @"C:\V\Creator\Show 1.mkv", 1, ".mkv");
        repo.UpsertVideo(s1, @"C:\V\Creator\Show 2.mkv", 2, ".mkv");

        // Override: split "Show 2.mkv" into a new series "Spin-off".
        repo.SetGroupingOverride(sec, @"C:\V\Creator\Show 2.mkv", "Spin-off", null);
        repo.RegroupSection(sec);

        var summaries = repo.GetSeriesSummaries(sec);
        // Should now have two series: "Show" (1 ep) and "Spin-off" (1 ep).
        summaries.Count.ShouldBe(2);
        summaries.ShouldContain(s => s.BaseTitle == "Show" && s.EpisodeCount == 1);
        summaries.ShouldContain(s => s.BaseTitle == "Spin-off" && s.EpisodeCount == 1);
    }

    // ── RegroupSection: merge ─────────────────────────────────────────────────

    [Fact]
    public void RegroupSection_Merge_FoldsTwoSeriesIntoOne()
    {
        var (db, repo, sec) = NewFx(); using var _ = db;

        // Two series: "Alpha" and "Beta".
        var s1 = repo.UpsertSeries(sec, "Alpha", false);
        repo.UpsertVideo(s1, @"C:\V\Creator\Alpha 1.mkv", 1, ".mkv");
        var s2 = repo.UpsertSeries(sec, "Beta", true);
        repo.UpsertVideo(s2, @"C:\V\Creator\Beta.mkv", 1, ".mkv");

        // Override: redirect "Beta.mkv" into "Alpha".
        repo.SetGroupingOverride(sec, @"C:\V\Creator\Beta.mkv", "Alpha", null);
        repo.RegroupSection(sec);

        var summaries = repo.GetSeriesSummaries(sec);
        // Should now have one series "Alpha" with 2 episodes.
        summaries.Count(s => s.EpisodeCount > 0).ShouldBe(1, "Beta merged into Alpha");
        var alpha = summaries.Single(s => s.EpisodeCount > 0);
        alpha.BaseTitle.ShouldBe("Alpha");
        alpha.EpisodeCount.ShouldBe(2);
    }

    // ── RegroupSection: manual episode_no ────────────────────────────────────

    [Fact]
    public void RegroupSection_ManualEpisodeNo_Persists()
    {
        var (db, repo, sec) = NewFx(); using var _ = db;

        var s1 = repo.UpsertSeries(sec, "Show", false);
        var vidId = repo.UpsertVideo(s1, @"C:\V\Creator\Show 1.mkv", 1, ".mkv");

        // Override: force this episode to number 99.
        repo.SetGroupingOverride(sec, @"C:\V\Creator\Show 1.mkv", null, 99);
        repo.RegroupSection(sec);

        var episodes = repo.GetEpisodes(s1);
        // The episode_no in the DB should now be 99.
        episodes.Single().EpisodeNo.ShouldBe(99);
    }

    // ── RegroupSection: idempotent ────────────────────────────────────────────

    [Fact]
    public void RegroupSection_RunTwice_IsIdempotent()
    {
        var (db, repo, sec) = NewFx(); using var _ = db;

        var s1 = repo.UpsertSeries(sec, "Show", false);
        repo.UpsertVideo(s1, @"C:\V\Creator\Show 1.mkv", 1, ".mkv");
        repo.UpsertVideo(s1, @"C:\V\Creator\Show 2.mkv", 2, ".mkv");
        repo.SetGroupingOverride(sec, @"C:\V\Creator\Show 2.mkv", "Spin-off", null);

        repo.RegroupSection(sec);
        var after1 = repo.GetSeriesSummaries(sec).OrderBy(s => s.BaseTitle).ToList();

        repo.RegroupSection(sec);
        var after2 = repo.GetSeriesSummaries(sec).OrderBy(s => s.BaseTitle).ToList();

        after2.Count.ShouldBe(after1.Count);
        for (var i = 0; i < after1.Count; i++)
        {
            after2[i].BaseTitle.ShouldBe(after1[i].BaseTitle);
            after2[i].EpisodeCount.ShouldBe(after1[i].EpisodeCount);
        }
    }

    // ── RegroupSection: no-op on empty section ────────────────────────────────

    [Fact]
    public void RegroupSection_EmptySection_DoesNotThrow()
    {
        var (db, repo, sec) = NewFx(); using var _ = db;
        // No videos — should return immediately without error.
        Should.NotThrow(() => repo.RegroupSection(sec));
    }
}
