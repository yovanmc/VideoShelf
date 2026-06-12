// tests/VideoShelf.Core.Tests/CanonicalNamerTests.cs
using Shouldly;
using VideoShelf.Core.Naming;
using VideoShelf.Core.Renaming;
using Xunit;

namespace VideoShelf.Core.Tests;

public class CanonicalNamerTests
{
    [Fact]
    public void Build_NumbersEpisodes_WithZeroPadding()
        => CanonicalNamer.Build("My Show", 3, ".mkv", 2).ShouldBe("My Show 03.mkv");

    [Fact]
    public void Build_Standalone_HasNoNumber()
        => CanonicalNamer.Build("My Movie", null, ".mp4", 2).ShouldBe("My Movie.mp4");

    [Fact]
    public void Build_AddsLeadingDot_WhenExtensionMissingIt()
        => CanonicalNamer.Build("X", 1, "mkv", 2).ShouldBe("X 01.mkv");

    [Fact]
    public void Build_SanitizesIllegalCharacters_AndCollapsesWhitespace()
        => CanonicalNamer.Build("A: B / C", 1, ".mkv", 2).ShouldBe("A B C 01.mkv");

    [Fact]
    public void Build_FallsBackToUntitled_WhenTitleSanitizesEmpty()
        => CanonicalNamer.Build("///", null, ".mkv", 2).ShouldBe("untitled.mkv");

    [Fact]
    public void PadWidth_IsAtLeastTwo_AndGrowsWithMax()
    {
        CanonicalNamer.PadWidth(new[] { 1, 2, 9 }).ShouldBe(2);
        CanonicalNamer.PadWidth(new[] { 1, 120 }).ShouldBe(3);
        CanonicalNamer.PadWidth(new int[0]).ShouldBe(2);
    }

    [Fact]
    public void Build_ReparsesToSameTitleAndEpisode_ViaTitleParser()
    {
        var name = CanonicalNamer.Build("My Show", 4, ".mkv", 2); // "My Show 04.mkv"
        var parsed = TitleParser.Parse(System.IO.Path.GetFileNameWithoutExtension(name));
        parsed.BaseTitle.ShouldBe("My Show");
        parsed.EpisodeNumber.ShouldBe(4);
    }
}
