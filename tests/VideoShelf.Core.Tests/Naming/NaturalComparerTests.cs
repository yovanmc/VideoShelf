using System;
using System.Linq;
using Shouldly;
using VideoShelf.Core.Naming;

namespace VideoShelf.Core.Tests.Naming;

public class NaturalComparerTests
{
    [Fact]
    public void Orders_embedded_numbers_numerically()
    {
        var input = new[] { "Clip 10", "Clip 2", "Clip 1" };
        Array.Sort(input, new NaturalComparer());
        input.ShouldBe(new[] { "Clip 1", "Clip 2", "Clip 10" });
    }

    [Fact]
    public void Is_case_insensitive_for_letters()
    {
        new NaturalComparer().Compare("apple", "Apple").ShouldBe(0);
    }

    [Fact]
    public void Falls_back_to_text_when_no_numbers()
    {
        new NaturalComparer().Compare("alpha", "beta").ShouldBeLessThan(0);
    }
}
