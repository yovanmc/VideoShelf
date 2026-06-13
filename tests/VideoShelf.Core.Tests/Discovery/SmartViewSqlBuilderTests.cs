using System;
using System.Collections.Generic;
using Shouldly;
using VideoShelf.Core.Discovery;
using VideoShelf.Core.Storage;
using Xunit;

namespace VideoShelf.Core.Tests.Discovery;

public sealed class SmartViewSqlBuilderTests
{
    // Fixed "now" for all dateAdded tests: 2026-06-13 00:00:00 UTC
    private static readonly DateTimeOffset Now = new(2026, 6, 13, 0, 0, 0, TimeSpan.Zero);

    // ── helpers ─────────────────────────────────────────────────────────────

    private static (string Where, IReadOnlyList<(string Name, object Value)> Params) Build(
        string match, params SmartRule[] rules)
        => SmartViewSqlBuilder.Build(new SmartViewDefinition(match, rules), Now);

    // ── empty rules ──────────────────────────────────────────────────────────

    [Fact]
    public void EmptyRules_ReturnsEmptyWhereAndParams()
    {
        var (where, parms) = Build("all");
        where.ShouldBe(string.Empty);
        parms.ShouldBeEmpty();
    }

    [Fact]
    public void EmptyRules_AnyMatch_ReturnsEmptyWhereAndParams()
    {
        var (where, parms) = Build("any");
        where.ShouldBe(string.Empty);
        parms.ShouldBeEmpty();
    }

    // ── tag / is ─────────────────────────────────────────────────────────────

    [Fact]
    public void Tag_Is_ProducesExistsFragment()
    {
        var (where, parms) = Build("all", new SmartRule("tag", "is", "Action"));

        // Exactly one param
        parms.Count.ShouldBe(1);
        parms[0].Name.ShouldBe("$p0");
        // Value is normalized
        parms[0].Value.ShouldBe(TagRepository.Normalize("Action"));

        // WHERE is wrapped in outer parens
        where.ShouldStartWith("(");
        where.ShouldEndWith(")");

        // Fragment contains EXISTS
        where.ShouldContain("EXISTS");
        // References $p0 three times (video_tags, series_tags, section_tags)
        CountOccurrences(where, "$p0").ShouldBe(3);
        where.ShouldContain("vt.video_id = v.id AND vt.tag = $p0");
        where.ShouldContain("st.series_id = v.series_id AND st.tag = $p0");
        where.ShouldContain("sect.section_id = s.section_id AND sect.tag = $p0");
    }

    [Fact]
    public void Tag_Is_NormalizesValue()
    {
        var (_, parms) = Build("all", new SmartRule("tag", "is", "  Sci Fi  "));
        parms[0].Value.ShouldBe("sci fi");
    }

    // ── tag / isNot ──────────────────────────────────────────────────────────

    [Fact]
    public void Tag_IsNot_ProducesNotExistsFragment()
    {
        var (where, parms) = Build("all", new SmartRule("tag", "isNot", "Horror"));

        parms.Count.ShouldBe(1);
        parms[0].Name.ShouldBe("$p0");
        parms[0].Value.ShouldBe(TagRepository.Normalize("Horror"));

        where.ShouldContain("NOT (");
        where.ShouldContain("EXISTS");
        CountOccurrences(where, "$p0").ShouldBe(3);
    }

    [Fact]
    public void Tag_IsNot_OneParamEntryDespiteThreeUsages()
    {
        var (_, parms) = Build("all", new SmartRule("tag", "isNot", "Drama"));
        parms.Count.ShouldBe(1);
    }

    // ── creator / is ─────────────────────────────────────────────────────────

    [Fact]
    public void Creator_Is_ProducesSectionIdEquality()
    {
        var (where, parms) = Build("all", new SmartRule("creator", "is", "42"));

        parms.Count.ShouldBe(1);
        parms[0].Name.ShouldBe("$p0");
        parms[0].Value.ShouldBe(42L);

        where.ShouldContain("s.section_id = $p0");
        where.ShouldNotContain("<>");
    }

    // ── creator / isNot ──────────────────────────────────────────────────────

    [Fact]
    public void Creator_IsNot_ProducesSectionIdInequality()
    {
        var (where, parms) = Build("all", new SmartRule("creator", "isNot", "7"));

        parms.Count.ShouldBe(1);
        parms[0].Value.ShouldBe(7L);
        where.ShouldContain("s.section_id <> $p0");
    }

    // ── watched / is ─────────────────────────────────────────────────────────

    [Fact]
    public void Watched_Is_True_Produces1()
    {
        var (where, parms) = Build("all", new SmartRule("watched", "is", "true"));

        parms.Count.ShouldBe(1);
        parms[0].Value.ShouldBe(1L);
        where.ShouldContain("v.watched = $p0");
    }

    [Fact]
    public void Watched_Is_False_Produces0()
    {
        var (where, parms) = Build("all", new SmartRule("watched", "is", "false"));

        parms.Count.ShouldBe(1);
        parms[0].Value.ShouldBe(0L);
        where.ShouldContain("v.watched = $p0");
    }

    // ── dateAdded / withinDays ───────────────────────────────────────────────

    [Fact]
    public void DateAdded_WithinDays_ProducesGteFragment()
    {
        var (where, parms) = Build("all", new SmartRule("dateAdded", "withinDays", "30"));

        parms.Count.ShouldBe(1);
        parms[0].Name.ShouldBe("$p0");

        // Cutoff: 2026-06-13 minus 30 days = 2026-05-14 in "o" format
        var expected = Now.AddDays(-30).ToString("o");
        parms[0].Value.ShouldBe(expected);

        where.ShouldContain("v.added_at >= $p0");
    }

    [Fact]
    public void DateAdded_BeforeDays_ProducesLtFragment()
    {
        var (where, parms) = Build("all", new SmartRule("dateAdded", "beforeDays", "7"));

        parms.Count.ShouldBe(1);
        var expected = Now.AddDays(-7).ToString("o");
        parms[0].Value.ShouldBe(expected);

        where.ShouldContain("v.added_at < $p0");
    }

    [Fact]
    public void DateAdded_CutoffMatchesLibraryRepositoryFormat()
    {
        // The format must be "o" (ISO 8601 round-trip) to match LibraryRepository.UpsertVideo.
        var (_, parms) = Build("all", new SmartRule("dateAdded", "withinDays", "1"));
        var cutoff = parms[0].Value as string;
        cutoff.ShouldNotBeNull();
        // "o" produces a string like "2026-06-12T00:00:00.0000000+00:00"
        cutoff.ShouldMatch(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}");
    }

    // ── duration / gt ────────────────────────────────────────────────────────

    [Fact]
    public void Duration_Gt_ProducesNullGuardedFragment()
    {
        var (where, parms) = Build("all", new SmartRule("duration", "gt", "3600"));

        parms.Count.ShouldBe(1);
        parms[0].Value.ShouldBe(3600L);
        where.ShouldContain("v.duration IS NOT NULL");
        where.ShouldContain("v.duration > $p0");
    }

    // ── duration / lt ────────────────────────────────────────────────────────

    [Fact]
    public void Duration_Lt_ProducesNullGuardedFragment()
    {
        var (where, parms) = Build("all", new SmartRule("duration", "lt", "600"));

        parms.Count.ShouldBe(1);
        parms[0].Value.ShouldBe(600L);
        where.ShouldContain("v.duration IS NOT NULL");
        where.ShouldContain("v.duration < $p0");
    }

    // ── match = "all" (AND join) ─────────────────────────────────────────────

    [Fact]
    public void MatchAll_MultipleRules_JoinedWithAnd()
    {
        var (where, parms) = Build("all",
            new SmartRule("watched", "is", "true"),
            new SmartRule("duration", "gt", "1800"));

        parms.Count.ShouldBe(2);
        parms[0].Name.ShouldBe("$p0");
        parms[1].Name.ShouldBe("$p1");

        where.ShouldContain(" AND ");
        where.ShouldNotContain(" OR ");
    }

    [Fact]
    public void MatchAll_CaseInsensitive()
    {
        var (where, _) = Build("ALL",
            new SmartRule("watched", "is", "true"),
            new SmartRule("watched", "is", "false"));
        where.ShouldContain(" AND ");
    }

    // ── match = "any" (OR join) ──────────────────────────────────────────────

    [Fact]
    public void MatchAny_MultipleRules_JoinedWithOr()
    {
        var (where, parms) = Build("any",
            new SmartRule("watched", "is", "true"),
            new SmartRule("duration", "lt", "300"));

        parms.Count.ShouldBe(2);
        parms[0].Name.ShouldBe("$p0");
        parms[1].Name.ShouldBe("$p1");

        where.ShouldContain(" OR ");
        // The whole result is wrapped
        where.ShouldStartWith("(");
        where.ShouldEndWith(")");
    }

    [Fact]
    public void MatchAny_CaseInsensitive()
    {
        var (where, _) = Build("ANY",
            new SmartRule("watched", "is", "true"),
            new SmartRule("watched", "is", "false"));
        where.ShouldContain(" OR ");
    }

    // ── sequential param indices ─────────────────────────────────────────────

    [Fact]
    public void ThreeRules_ParamNamesAreP0P1P2()
    {
        var (_, parms) = Build("all",
            new SmartRule("tag", "is", "anime"),
            new SmartRule("creator", "is", "5"),
            new SmartRule("watched", "is", "false"));

        parms.Count.ShouldBe(3);
        parms[0].Name.ShouldBe("$p0");
        parms[1].Name.ShouldBe("$p1");
        parms[2].Name.ShouldBe("$p2");
    }

    // ── single rule output wrapped in parens ─────────────────────────────────

    [Fact]
    public void SingleRule_WhereIsWrappedInParens()
    {
        var (where, _) = Build("all", new SmartRule("watched", "is", "true"));
        where.ShouldStartWith("(");
        where.ShouldEndWith(")");
    }

    // ── unknown field → ArgumentException ────────────────────────────────────

    [Fact]
    public void UnknownField_ThrowsArgumentException()
    {
        Should.Throw<ArgumentException>(() =>
            Build("all", new SmartRule("rating", "is", "5")));
    }

    // ── unknown op → ArgumentException ───────────────────────────────────────

    [Fact]
    public void UnknownOp_Tag_ThrowsArgumentException()
    {
        Should.Throw<ArgumentException>(() =>
            Build("all", new SmartRule("tag", "contains", "action")));
    }

    [Fact]
    public void UnknownOp_Creator_ThrowsArgumentException()
    {
        Should.Throw<ArgumentException>(() =>
            Build("all", new SmartRule("creator", "contains", "10")));
    }

    [Fact]
    public void UnknownOp_Watched_ThrowsArgumentException()
    {
        Should.Throw<ArgumentException>(() =>
            Build("all", new SmartRule("watched", "isNot", "true")));
    }

    [Fact]
    public void UnknownOp_DateAdded_ThrowsArgumentException()
    {
        Should.Throw<ArgumentException>(() =>
            Build("all", new SmartRule("dateAdded", "is", "30")));
    }

    [Fact]
    public void UnknownOp_Duration_ThrowsArgumentException()
    {
        Should.Throw<ArgumentException>(() =>
            Build("all", new SmartRule("duration", "gte", "3600")));
    }

    // ── unknown Match → ArgumentException ────────────────────────────────────

    [Fact]
    public void UnknownMatch_ThrowsArgumentException()
    {
        Should.Throw<ArgumentException>(() =>
            Build("none", new SmartRule("watched", "is", "true")));
    }

    [Fact]
    public void UnknownMatch_EmptyString_ThrowsArgumentException()
    {
        Should.Throw<ArgumentException>(() =>
            Build("", new SmartRule("watched", "is", "true")));
    }

    // ── tag rule emits exactly one param entry ───────────────────────────────

    [Fact]
    public void TagRule_ExactlyOneParamEntryForThreeUsages()
    {
        // The EXISTS union references $p0 three times but params list has only one entry
        var (where, parms) = Build("all", new SmartRule("tag", "is", "sci-fi"));

        parms.Count.ShouldBe(1, "one param per rule, reused N times in the fragment");
        CountOccurrences(where, "$p0").ShouldBe(3, "$p0 appears in each of the 3 UNION branches");
    }

    [Fact]
    public void TagIsNot_ExactlyOneParamEntryForThreeUsages()
    {
        var (where, parms) = Build("all", new SmartRule("tag", "isNot", "Documentary"));

        parms.Count.ShouldBe(1);
        CountOccurrences(where, "$p0").ShouldBe(3);
    }

    // ── mixed rules, correct param values ────────────────────────────────────

    [Fact]
    public void MixedRules_CorrectParamValues()
    {
        var (_, parms) = Build("any",
            new SmartRule("tag", "is", "Anime"),
            new SmartRule("creator", "isNot", "99"),
            new SmartRule("dateAdded", "withinDays", "14"),
            new SmartRule("duration", "lt", "900"),
            new SmartRule("watched", "is", "false"));

        parms.Count.ShouldBe(5);
        parms[0].Value.ShouldBe(TagRepository.Normalize("Anime"));
        parms[1].Value.ShouldBe(99L);
        parms[2].Value.ShouldBe(Now.AddDays(-14).ToString("o"));
        parms[3].Value.ShouldBe(900L);
        parms[4].Value.ShouldBe(0L);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static int CountOccurrences(string source, string target)
    {
        int count = 0;
        int index = 0;
        while ((index = source.IndexOf(target, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += target.Length;
        }
        return count;
    }
}
