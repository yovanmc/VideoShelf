using Shouldly;
using VideoShelf.Core.Search;

namespace VideoShelf.Core.Tests.Search;

public sealed class PaletteRankerTests
{
    // ── Non-matches (score = 0) ────────────────────────────────────────────────

    [Fact]
    public void Empty_query_returns_zero()
        => PaletteRanker.Score("", "Home").ShouldBe(0);

    [Fact]
    public void Whitespace_only_query_returns_zero()
        => PaletteRanker.Score("   ", "Home").ShouldBe(0);

    [Fact]
    public void Non_subsequence_returns_zero()
        => PaletteRanker.Score("xyz", "Home").ShouldBe(0);

    [Fact]
    public void Query_longer_than_candidate_returns_zero_when_no_match()
        => PaletteRanker.Score("HomeBrowseSettingsExtra", "Home").ShouldBe(0);

    // ── Exact match (score = 1.0) ──────────────────────────────────────────────

    [Fact]
    public void Exact_match_returns_one()
        => PaletteRanker.Score("Home", "Home").ShouldBe(1.0);

    [Fact]
    public void Exact_match_case_insensitive()
        => PaletteRanker.Score("home", "Home").ShouldBe(1.0);

    [Fact]
    public void Exact_match_mixed_case()
        => PaletteRanker.Score("SETTINGS", "Settings").ShouldBe(1.0);

    // ── Prefix match (score = 0.9) ─────────────────────────────────────────────

    [Fact]
    public void Prefix_match_returns_0_9()
        => PaletteRanker.Score("Hom", "Home").ShouldBe(0.9);

    [Fact]
    public void Prefix_match_case_insensitive()
        => PaletteRanker.Score("set", "Settings").ShouldBe(0.9);

    // ── Word-boundary match (score = 0.75) ────────────────────────────────────

    [Fact]
    public void Word_boundary_match_second_word()
        => PaletteRanker.Score("View", "Smart Views").ShouldBe(0.75);

    [Fact]
    public void Word_boundary_match_case_insensitive()
        => PaletteRanker.Score("que", "Up Next / Queue").ShouldBe(0.75);

    // ── Ordering: exact > prefix > word-boundary > subsequence ────────────────

    [Fact]
    public void Exact_beats_prefix_beats_word_boundary_beats_subsequence()
    {
        var exact       = PaletteRanker.Score("Smart Views", "Smart Views");
        var prefix      = PaletteRanker.Score("Smart", "Smart Views");
        var wordBound   = PaletteRanker.Score("View", "Smart Views");
        var subseq      = PaletteRanker.Score("sv", "Smart Views");

        exact.ShouldBeGreaterThan(prefix);
        prefix.ShouldBeGreaterThan(wordBound);
        wordBound.ShouldBeGreaterThan(subseq);
        subseq.ShouldBeGreaterThan(0);
    }

    // ── Subsequence scoring ────────────────────────────────────────────────────

    [Fact]
    public void Subsequence_match_returns_positive_score()
    {
        var score = PaletteRanker.Score("hm", "Home");
        score.ShouldBeGreaterThan(0);
        score.ShouldBeLessThan(0.9); // below prefix tier
    }

    [Fact]
    public void Subsequence_score_below_word_boundary()
    {
        var wb  = PaletteRanker.Score("Brow", "Browse");
        var sub = PaletteRanker.Score("bse", "Browse");
        wb.ShouldBeGreaterThan(sub);
    }

    [Fact]
    public void Shorter_candidate_with_same_subsequence_scores_higher()
    {
        // Query "ws" is a subsequence of both candidates but NOT a prefix of either.
        // "Browse" is shorter, so it should score higher than the long string.
        var shorter = PaletteRanker.Score("ws", "Browse");
        var longer  = PaletteRanker.Score("ws", "Browse through your collection of videos");
        shorter.ShouldBeGreaterThan(0);
        longer.ShouldBeGreaterThan(0);
        shorter.ShouldBeGreaterThan(longer);
    }
}
