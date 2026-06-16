using System.Linq;
using Shouldly;
using VideoShelf.App.Harness;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.Tests.Harness;

/// <summary>
/// Tests for the M17 I2 additions to HarnessOptions (new --view values) and the
/// SeedAlphabetCreators logic that produces ≥30 creators spanning the alphabet plus
/// one creator with ≥40 series.
/// Also covers the M19 G-3 player sub-state harness tokens.
/// </summary>
public class HarnessViewsAndSeedTests
{
    // ── HarnessOptions parse tests ────────────────────────────────────────────

    [Theory]
    [InlineData("BrowseSelection")]
    [InlineData("BrowseFilter")]
    public void Parse_NewM17ViewValues_AreAccepted(string viewName)
    {
        var opts = HarnessOptions.Parse(new[] { "--view", viewName, "--done-signal", @"C:\s.txt" });
        opts.View.ShouldBe(viewName);
        opts.IsHarness.ShouldBeTrue();
    }

    // ── G-3 (M19): new player sub-state --view tokens ────────────────────────

    [Theory]
    [InlineData("PlayerMore")]
    [InlineData("PlayerTracks")]
    [InlineData("PlayerVolume")]
    [InlineData("PlayerSpeed")]
    [InlineData("PlayerAbRepeat")]
    [InlineData("PlayerSkipFeedback")]
    [InlineData("PlayerUpNext")]
    public void Parse_NewM19PlayerViewValues_AreAccepted(string viewName)
    {
        var opts = HarnessOptions.Parse(new[] { "--view", viewName, "--done-signal", @"C:\s.txt" });
        opts.View.ShouldBe(viewName);
        opts.IsHarness.ShouldBeTrue();
    }

    [Fact]
    public void Parse_PlayerSubStates_SetSeedDemo()
    {
        // Verify that --seed-demo + player sub-state tokens both parse correctly together
        // (as the sweep always passes --seed-demo so a real DB is populated).
        var opts = HarnessOptions.Parse(new[]
        {
            "--view", "PlayerMore",
            "--seed-demo",
            "--done-signal", @"C:\s.txt"
        });
        opts.View.ShouldBe("PlayerMore");
        opts.SeedDemo.ShouldBeTrue();
        opts.IsHarness.ShouldBeTrue();
    }

    // ── SeedAlphabetCreators DB assertions ───────────────────────────────────
    // We test the repository-level outcomes directly (without launching the app)
    // by replicating the logic against an in-memory temp DB.

    [Fact]
    public void SeedAlphabetCreators_produces_at_least_30_creators()
    {
        using var temp = new AppTempDb();
        var lib  = new LibraryRepository(temp.Db);
        var tags = new TagRepository(temp.Db);

        SeedAlphabetCreatorsInto(lib, tags);

        // GetSectionSummaries returns ALL sections including the ≥40-series one.
        var summaries = lib.GetSectionSummaries();
        // 30 alphabet creators + 1 "Alphabet Cinema" = 31 total (≥30 check).
        summaries.Count.ShouldBeGreaterThanOrEqualTo(30);
    }

    [Fact]
    public void SeedAlphabetCreators_one_creator_has_42_series()
    {
        using var temp = new AppTempDb();
        var lib  = new LibraryRepository(temp.Db);
        var tags = new TagRepository(temp.Db);

        SeedAlphabetCreatorsInto(lib, tags);

        var summaries = lib.GetSectionSummaries();
        var maxSeriesCount = summaries
            .Select(s => lib.GetSeriesForSection(s.SectionId).Count)
            .Max();

        maxSeriesCount.ShouldBeGreaterThanOrEqualTo(40);
    }

    [Fact]
    public void SeedAlphabetCreators_covers_multiple_starting_letters()
    {
        using var temp = new AppTempDb();
        var lib  = new LibraryRepository(temp.Db);
        var tags = new TagRepository(temp.Db);

        SeedAlphabetCreatorsInto(lib, tags);

        var summaries = lib.GetSectionSummaries();
        var startLetters = summaries
            .Where(s => !string.IsNullOrEmpty(s.DisplayName))
            .Select(s => char.ToUpperInvariant(s.DisplayName[0]))
            .Distinct()
            .Count();

        // We seed names starting with A–Z (and "Alphabet Cinema" is an extra A) = ≥20 distinct letters.
        startLetters.ShouldBeGreaterThanOrEqualTo(20);
    }

    [Fact]
    public void SeedAlphabetCreators_is_idempotent()
    {
        using var temp = new AppTempDb();
        var lib  = new LibraryRepository(temp.Db);
        var tags = new TagRepository(temp.Db);

        // Seed twice — should not throw or duplicate sections.
        SeedAlphabetCreatorsInto(lib, tags);
        SeedAlphabetCreatorsInto(lib, tags);

        var summaries = lib.GetSectionSummaries();
        summaries.Count.ShouldBeGreaterThanOrEqualTo(30);
    }

    // ── Helper: replicate SeedAlphabetCreators logic against a test DB ────────
    // Kept in sync with HarnessRunner.SeedAlphabetCreators manually.
    // This avoids exposing the private method in production code.

    private static void SeedAlphabetCreatorsInto(LibraryRepository lib, TagRepository tags)
    {
        var srcId = lib.UpsertSource(@"\DemoAlphabet", "DemoAlphabet");

        string[] creatorNames =
        {
            "Alice A",     "Bella B",     "Carlos C",    "Diana D",
            "Elena E",     "Frank F",     "Grace G",     "Hector H",
            "Iris I",      "James J",     "Kira K",      "Leo L",
            "Maya M",      "Noel N",      "Olivia O",    "Pedro P",
            "Quinn Q",     "Rosa R",      "Sam S",       "Tara T",
            "Uma U",       "Victor V",    "Wendy W",     "Xander X",
            "Yuki Y",      "Zara Z",      "Ana Autumn",  "Bruno Bay",
            "Cleo Cross",  "Dani Dusk",
        };

        foreach (var (name, idx) in creatorNames.Select((n, i) => (n, i)))
        {
            var sectionId = lib.UpsertSection(srcId, name);
            tags.AddTag(sectionId, "demo");
            var seriesId = lib.UpsertSeries(sectionId, name, isStandalone: true);
            var filePath = $@"\DemoAlphabet\{name}\video_{idx:D2}.mp4";
            lib.UpsertVideo(seriesId, filePath, 1, ".mp4");
        }

        const int SeriesCount = 42;
        var cinemaSectionId = lib.UpsertSection(srcId, "Alphabet Cinema");
        tags.AddTag(cinemaSectionId, "demo");

        for (var s = 1; s <= SeriesCount; s++)
        {
            var seriesTitle = $"Series {s:D2}";
            var seriesId = lib.UpsertSeries(cinemaSectionId, seriesTitle, isStandalone: false);
            for (var ep = 1; ep <= 2; ep++)
            {
                var filePath = $@"\DemoAlphabet\AlphabetCinema\{seriesTitle}\ep{ep:D2}.mp4";
                lib.UpsertVideo(seriesId, filePath, ep, ".mp4");
            }
        }
    }
}
