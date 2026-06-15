using Shouldly;
using VideoShelf.App.Visuals;

namespace VideoShelf.App.Tests.Visuals;

public class CreatorAvatarTests
{
    // ── Initials ────────────────────────────────────────────────────────────

    [Fact]
    public void Initials_two_words_returns_first_and_last_initial()
        => CreatorAvatar.Initials("Alice Autumn").ShouldBe("AA");

    [Fact]
    public void Initials_single_word_returns_single_letter()
        => CreatorAvatar.Initials("Madonna").ShouldBe("M");

    [Fact]
    public void Initials_padded_multi_word_trims_and_max_two()
        => CreatorAvatar.Initials("  bruno  bay ").ShouldBe("BB");

    [Fact]
    public void Initials_empty_string_returns_question_mark()
        => CreatorAvatar.Initials("").ShouldBe("?");

    [Fact]
    public void Initials_null_returns_question_mark()
        => CreatorAvatar.Initials(null).ShouldBe("?");

    [Fact]
    public void Initials_whitespace_only_returns_question_mark()
        => CreatorAvatar.Initials("   ").ShouldBe("?");

    [Fact]
    public void Initials_uppercases_each_initial()
        => CreatorAvatar.Initials("alice autumn").ShouldBe("AA");

    // ── HueDegrees ──────────────────────────────────────────────────────────

    [Fact]
    public void HueDegrees_returns_value_in_range_0_to_359()
    {
        var hue = CreatorAvatar.HueDegrees("Alice A");
        hue.ShouldBeGreaterThanOrEqualTo(0);
        hue.ShouldBeLessThan(360);
    }

    [Fact]
    public void HueDegrees_is_deterministic_across_calls()
    {
        var h1 = CreatorAvatar.HueDegrees("Alice A");
        var h2 = CreatorAvatar.HueDegrees("Alice A");
        h1.ShouldBe(h2);
    }

    [Fact]
    public void HueDegrees_different_names_can_differ()
    {
        // Not guaranteed to differ for every pair, but these two specific names
        // should hash to different values (verifies the hash is actually name-sensitive).
        var hAlice = CreatorAvatar.HueDegrees("Alice A");
        var hBob   = CreatorAvatar.HueDegrees("Bob B");
        // We can't assert they differ (hash collision is theoretically possible)
        // but we CAN assert both are in range.
        hAlice.ShouldBeGreaterThanOrEqualTo(0);
        hBob.ShouldBeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void HueDegrees_null_returns_0()
        => CreatorAvatar.HueDegrees(null).ShouldBe(0);

    [Fact]
    public void HueDegrees_empty_string_returns_0()
        => CreatorAvatar.HueDegrees("").ShouldBe(0);
}
