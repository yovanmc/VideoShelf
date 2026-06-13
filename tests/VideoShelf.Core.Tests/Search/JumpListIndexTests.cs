using System.Collections.Generic;
using Shouldly;
using VideoShelf.Core.Search;
using Xunit;

namespace VideoShelf.Core.Tests.Search;

/// <summary>G3 — Unit tests for the pure A–Z jump-list helper.</summary>
public class JumpListIndexTests
{
    // ── FirstIndexForLetter ─────────────────────────────────────────────────

    [Fact]
    public void FirstIndexForLetter_returns_index_of_first_match()
    {
        var names = new List<string> { "Alpha", "Bravo", "Charlie", "Archer" };

        JumpListIndex.FirstIndexForLetter(names, 'A').ShouldBe(0);
        JumpListIndex.FirstIndexForLetter(names, 'B').ShouldBe(1);
        JumpListIndex.FirstIndexForLetter(names, 'C').ShouldBe(2);
    }

    [Fact]
    public void FirstIndexForLetter_is_case_insensitive()
    {
        var names = new List<string> { "alpha", "beta" };

        JumpListIndex.FirstIndexForLetter(names, 'A').ShouldBe(0);
        JumpListIndex.FirstIndexForLetter(names, 'a').ShouldBe(0);
        JumpListIndex.FirstIndexForLetter(names, 'B').ShouldBe(1);
        JumpListIndex.FirstIndexForLetter(names, 'b').ShouldBe(1);
    }

    [Fact]
    public void FirstIndexForLetter_returns_minus1_when_no_match()
    {
        var names = new List<string> { "Alpha", "Bravo", "Charlie" };

        JumpListIndex.FirstIndexForLetter(names, 'Z').ShouldBe(-1);
        JumpListIndex.FirstIndexForLetter(names, 'D').ShouldBe(-1);
    }

    [Fact]
    public void FirstIndexForLetter_returns_first_not_second_match()
    {
        // "Archer" comes after "Alpha" — should return the index of "Alpha" (0), not "Archer" (3).
        var names = new List<string> { "Alpha", "Bravo", "Charlie", "Archer" };

        JumpListIndex.FirstIndexForLetter(names, 'A').ShouldBe(0);
    }

    [Fact]
    public void FirstIndexForLetter_skips_empty_names()
    {
        var names = new List<string> { "", "Beta" };

        JumpListIndex.FirstIndexForLetter(names, 'B').ShouldBe(1);
        JumpListIndex.FirstIndexForLetter(names, 'A').ShouldBe(-1);
    }

    [Fact]
    public void FirstIndexForLetter_returns_minus1_on_empty_list()
    {
        JumpListIndex.FirstIndexForLetter(new List<string>(), 'A').ShouldBe(-1);
    }

    // ── AvailableLetters ────────────────────────────────────────────────────

    [Fact]
    public void AvailableLetters_returns_letters_in_alphabetical_order()
    {
        var names = new List<string> { "Charlie", "Alpha", "Bravo" };

        var letters = JumpListIndex.AvailableLetters(names);

        letters.ShouldBe(new[] { 'A', 'B', 'C' });
    }

    [Fact]
    public void AvailableLetters_excludes_non_alpha_leading_characters()
    {
        // Leading digit / symbol names are excluded from the A–Z buckets.
        var names = new List<string> { "1first", "!second", "Alpha" };

        var letters = JumpListIndex.AvailableLetters(names);

        letters.ShouldBe(new[] { 'A' });
    }

    [Fact]
    public void AvailableLetters_returns_empty_for_empty_list()
    {
        JumpListIndex.AvailableLetters(new List<string>()).ShouldBeEmpty();
    }
}
