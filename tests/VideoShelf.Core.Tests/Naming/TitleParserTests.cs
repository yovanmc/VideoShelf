using Shouldly;
using VideoShelf.Core.Naming;

namespace VideoShelf.Core.Tests.Naming;

public class TitleParserTests
{
    [Theory]
    // file stem -> expected base title, expected episode number (null = unnumbered)
    [InlineData("Cool Story", "Cool Story", null)]
    [InlineData("Cool Story 2 the sequel", "Cool Story", 2)]
    [InlineData("Cool Story 3 finale", "Cool Story", 3)]
    [InlineData("Another Standalone Tale", "Another Standalone Tale", null)]
    [InlineData("Cool Story 2", "Cool Story", 2)]
    [InlineData("Cool   Story   2", "Cool Story", 2)]   // collapse whitespace in base
    [InlineData("Episode 01", "Episode", 1)]            // leading zeros -> 1
    public void Parses_base_title_and_episode(string stem, string expectedBase, int? expectedEpisode)
    {
        var parsed = TitleParser.Parse(stem);
        parsed.BaseTitle.ShouldBe(expectedBase);
        parsed.EpisodeNumber.ShouldBe(expectedEpisode);
    }

    [Fact]
    public void First_token_number_is_not_an_episode_marker()
    {
        // "300" as the only/first token stays part of the title (no base before it).
        var parsed = TitleParser.Parse("300");
        parsed.BaseTitle.ShouldBe("300");
        parsed.EpisodeNumber.ShouldBeNull();
    }
}
