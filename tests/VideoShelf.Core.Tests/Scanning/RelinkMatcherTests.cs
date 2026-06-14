using System.Collections.Generic;
using Shouldly;
using VideoShelf.Core.Models;
using VideoShelf.Core.Scanning;

namespace VideoShelf.Core.Tests.Scanning;

/// <summary>M18-F: Unit tests for the pure <see cref="RelinkMatcher"/>.</summary>
public sealed class RelinkMatcherTests
{
    private static MissingVideo Missing(long id, long sizeBytes, double? duration = null)
        => new MissingVideo(id, @"C:\old\video.mp4", "Creator", "Series", sizeBytes, duration);

    private static CandidateFile Cand(string path, long size, double? dur = null)
        => new CandidateFile(path, size, dur);

    // ── Exact size match → single candidate returned ──────────────────────────

    [Fact]
    public void FindCandidate_returns_single_size_match()
    {
        var missing = Missing(1, sizeBytes: 1_000_000);
        var candidates = new List<CandidateFile>
        {
            Cand(@"C:\new\video.mp4", 1_000_000),
        };

        var result = RelinkMatcher.FindCandidate(missing, candidates);

        result.ShouldBe(@"C:\new\video.mp4");
    }

    // ── Ambiguous → null ──────────────────────────────────────────────────────

    [Fact]
    public void FindCandidate_returns_null_when_multiple_size_matches()
    {
        var missing = Missing(1, sizeBytes: 1_000_000);
        var candidates = new List<CandidateFile>
        {
            Cand(@"C:\new\a.mp4", 1_000_000),
            Cand(@"C:\new\b.mp4", 1_000_000),
        };

        var result = RelinkMatcher.FindCandidate(missing, candidates);

        result.ShouldBeNull();
    }

    // ── No match → null ───────────────────────────────────────────────────────

    [Fact]
    public void FindCandidate_returns_null_when_no_size_match()
    {
        var missing = Missing(1, sizeBytes: 1_000_000);
        var candidates = new List<CandidateFile>
        {
            Cand(@"C:\new\video.mp4", 500_000),
        };

        var result = RelinkMatcher.FindCandidate(missing, candidates);

        result.ShouldBeNull();
    }

    // ── Null size → null (can't auto-match) ───────────────────────────────────

    [Fact]
    public void FindCandidate_returns_null_when_missing_has_no_size()
    {
        var missing = new MissingVideo(1, @"C:\old\video.mp4", "Creator", "Series", SizeBytes: null);
        var candidates = new List<CandidateFile>
        {
            Cand(@"C:\new\video.mp4", 1_000_000),
        };

        var result = RelinkMatcher.FindCandidate(missing, candidates);

        result.ShouldBeNull();
    }

    // ── Duration narrows ambiguous size matches ────────────────────────────────

    [Fact]
    public void FindCandidate_uses_duration_to_disambiguate_two_size_equal_files()
    {
        var missing = Missing(1, sizeBytes: 1_000_000, duration: 120.4);
        var candidates = new List<CandidateFile>
        {
            Cand(@"C:\new\a.mp4", 1_000_000, dur: 120.0), // rounds to 120 — matches
            Cand(@"C:\new\b.mp4", 1_000_000, dur: 240.0), // different duration
        };

        var result = RelinkMatcher.FindCandidate(missing, candidates);

        result.ShouldBe(@"C:\new\a.mp4");
    }

    [Fact]
    public void FindCandidate_still_returns_null_when_duration_leaves_two_matches()
    {
        var missing = Missing(1, sizeBytes: 1_000_000, duration: 120.0);
        var candidates = new List<CandidateFile>
        {
            Cand(@"C:\new\a.mp4", 1_000_000, dur: 120.0),
            Cand(@"C:\new\b.mp4", 1_000_000, dur: 120.0),
        };

        var result = RelinkMatcher.FindCandidate(missing, candidates);

        result.ShouldBeNull();
    }

    // ── Duration falls back to size-only when candidates lack duration ─────────

    [Fact]
    public void FindCandidate_falls_back_to_size_only_when_candidates_lack_duration()
    {
        // Missing has duration; candidates don't — should not discard the unique size match.
        var missing = Missing(1, sizeBytes: 1_000_000, duration: 120.0);
        var candidates = new List<CandidateFile>
        {
            Cand(@"C:\new\video.mp4", 1_000_000, dur: null), // no duration info
        };

        var result = RelinkMatcher.FindCandidate(missing, candidates);

        result.ShouldBe(@"C:\new\video.mp4");
    }

    // ── Empty candidates list ─────────────────────────────────────────────────

    [Fact]
    public void FindCandidate_returns_null_on_empty_list()
    {
        var missing = Missing(1, sizeBytes: 1_000_000);
        var result = RelinkMatcher.FindCandidate(missing, new List<CandidateFile>());
        result.ShouldBeNull();
    }
}
