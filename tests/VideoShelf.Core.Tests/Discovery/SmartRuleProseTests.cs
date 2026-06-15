using System.Collections.Generic;
using Shouldly;
using VideoShelf.Core.Discovery;
using Xunit;

namespace VideoShelf.Core.Tests.Discovery;

public sealed class SmartRuleProseTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static SmartViewDefinition Def(string match, params SmartRule[] rules) =>
        new SmartViewDefinition(match, rules);

    private static string Prose(SmartViewDefinition def,
        IReadOnlyDictionary<long, string>? names = null) =>
        SmartRuleProse.Describe(def, names);

    // ── empty rules ──────────────────────────────────────────────────────────

    [Fact]
    public void NoRules_returns_no_rules_placeholder()
    {
        var result = Prose(Def("all"));
        result.ShouldBe("(no rules)");
    }

    // ── match prefix ─────────────────────────────────────────────────────────

    [Fact]
    public void MatchAll_prefix_is_All_of()
    {
        var result = Prose(Def("all", new SmartRule("watched", "is", "false")));
        result.ShouldStartWith("All of:");
    }

    [Fact]
    public void MatchAny_prefix_is_Any_of()
    {
        var result = Prose(Def("any", new SmartRule("watched", "is", "false")));
        result.ShouldStartWith("Any of:");
    }

    [Fact]
    public void UnknownMatch_falls_back_gracefully()
    {
        // Should not throw; raw token used in prefix.
        var result = Prose(Def("none", new SmartRule("watched", "is", "false")));
        result.ShouldStartWith("none of:");
    }

    // ── tag rules ────────────────────────────────────────────────────────────

    [Fact]
    public void Tag_is_renders_tagged_X()
    {
        var result = Prose(Def("all", new SmartRule("tag", "is", "anime")));
        result.ShouldBe("All of: tagged anime");
    }

    [Fact]
    public void Tag_isNot_renders_not_tagged_X()
    {
        var result = Prose(Def("all", new SmartRule("tag", "isNot", "horror")));
        result.ShouldBe("All of: not tagged horror");
    }

    [Fact]
    public void Tag_unknown_op_falls_back_to_raw()
    {
        var result = Prose(Def("all", new SmartRule("tag", "contains", "sci-fi")));
        result.ShouldBe("All of: tag contains sci-fi");
    }

    // ── creator rules ────────────────────────────────────────────────────────

    [Fact]
    public void Creator_is_with_name_map_renders_by_name()
    {
        var names = new Dictionary<long, string> { [42L] = "Studio Ghibli" };
        var result = Prose(Def("all", new SmartRule("creator", "is", "42")), names);
        result.ShouldBe("All of: by Studio Ghibli");
    }

    [Fact]
    public void Creator_isNot_with_name_map_renders_not_by_name()
    {
        var names = new Dictionary<long, string> { [7L] = "Disney" };
        var result = Prose(Def("all", new SmartRule("creator", "isNot", "7")), names);
        result.ShouldBe("All of: not by Disney");
    }

    [Fact]
    public void Creator_is_without_name_map_falls_back_to_creator_hash_id()
    {
        var result = Prose(Def("all", new SmartRule("creator", "is", "99")));
        result.ShouldBe("All of: by creator #99");
    }

    [Fact]
    public void Creator_is_with_missing_id_in_map_falls_back_to_creator_hash_id()
    {
        var names = new Dictionary<long, string> { [1L] = "Someone" };
        // ID 99 not in map
        var result = Prose(Def("all", new SmartRule("creator", "is", "99")), names);
        result.ShouldBe("All of: by creator #99");
    }

    [Fact]
    public void Creator_unknown_op_falls_back_to_raw()
    {
        var result = Prose(Def("all", new SmartRule("creator", "contains", "5")));
        result.ShouldBe("All of: creator contains 5");
    }

    // ── watched rules ─────────────────────────────────────────────────────────

    [Fact]
    public void Watched_is_true_renders_watched()
    {
        var result = Prose(Def("all", new SmartRule("watched", "is", "true")));
        result.ShouldBe("All of: watched");
    }

    [Fact]
    public void Watched_is_false_renders_unwatched()
    {
        var result = Prose(Def("all", new SmartRule("watched", "is", "false")));
        result.ShouldBe("All of: unwatched");
    }

    [Fact]
    public void Watched_unknown_op_falls_back_to_raw()
    {
        var result = Prose(Def("all", new SmartRule("watched", "isNot", "true")));
        result.ShouldBe("All of: watched isNot true");
    }

    // ── dateAdded rules ───────────────────────────────────────────────────────

    [Fact]
    public void DateAdded_withinDays_renders_added_in_the_last_N_days()
    {
        var result = Prose(Def("all", new SmartRule("dateAdded", "withinDays", "30")));
        result.ShouldBe("All of: added in the last 30 days");
    }

    [Fact]
    public void DateAdded_beforeDays_renders_added_more_than_N_days_ago()
    {
        var result = Prose(Def("all", new SmartRule("dateAdded", "beforeDays", "365")));
        result.ShouldBe("All of: added more than 365 days ago");
    }

    [Fact]
    public void DateAdded_unknown_op_falls_back_to_raw()
    {
        var result = Prose(Def("all", new SmartRule("dateAdded", "is", "30")));
        result.ShouldBe("All of: dateAdded is 30");
    }

    // ── duration rules ────────────────────────────────────────────────────────

    [Fact]
    public void Duration_gt_renders_longer_than_with_human_duration()
    {
        // 5400 seconds = 1h 30m
        var result = Prose(Def("all", new SmartRule("duration", "gt", "5400")));
        result.ShouldBe("All of: longer than 1h 30m");
    }

    [Fact]
    public void Duration_lt_renders_shorter_than_with_human_duration()
    {
        // 1800 seconds = 30 min
        var result = Prose(Def("all", new SmartRule("duration", "lt", "1800")));
        result.ShouldBe("All of: shorter than 30 min");
    }

    [Fact]
    public void Duration_unknown_op_falls_back_to_raw()
    {
        var result = Prose(Def("all", new SmartRule("duration", "gte", "3600")));
        result.ShouldBe("All of: duration gte 3600");
    }

    // ── HumanDuration helper ─────────────────────────────────────────────────

    [Theory]
    [InlineData(0L,    "0s")]
    [InlineData(1L,    "1s")]
    [InlineData(45L,   "45s")]
    [InlineData(59L,   "59s")]
    [InlineData(60L,   "1 min")]
    [InlineData(90L,   "1 min")]
    [InlineData(1800L, "30 min")]
    [InlineData(3540L, "59 min")]
    [InlineData(3600L, "1h")]
    [InlineData(5400L, "1h 30m")]
    [InlineData(7200L, "2h")]
    [InlineData(7260L, "2h 1m")]
    public void HumanDuration_formats_correctly(long seconds, string expected)
    {
        SmartRuleProse.HumanDuration(seconds).ShouldBe(expected);
    }

    // ── unknown field ─────────────────────────────────────────────────────────

    [Fact]
    public void Unknown_field_falls_back_to_raw_token()
    {
        var result = Prose(Def("all", new SmartRule("rating", "is", "5")));
        result.ShouldBe("All of: rating is 5");
    }

    // ── multiple rules joined ─────────────────────────────────────────────────

    [Fact]
    public void Multiple_rules_are_joined_with_comma_space()
    {
        var result = Prose(Def("all",
            new SmartRule("tag", "is", "anime"),
            new SmartRule("watched", "is", "false"),
            new SmartRule("duration", "gt", "1800")));
        result.ShouldBe("All of: tagged anime, unwatched, longer than 30 min");
    }

    [Fact]
    public void Any_match_with_multiple_rules_joined_with_comma_space()
    {
        var result = Prose(Def("any",
            new SmartRule("watched", "is", "true"),
            new SmartRule("dateAdded", "withinDays", "7")));
        result.ShouldBe("Any of: watched, added in the last 7 days");
    }

    // ── creatorNames map (id→name) ────────────────────────────────────────────

    [Fact]
    public void CreatorNames_map_resolves_multiple_ids()
    {
        var names = new Dictionary<long, string>
        {
            [10L] = "Creator A",
            [20L] = "Creator B",
        };
        var result = Prose(Def("any",
            new SmartRule("creator", "is", "10"),
            new SmartRule("creator", "isNot", "20")), names);
        result.ShouldBe("Any of: by Creator A, not by Creator B");
    }
}
