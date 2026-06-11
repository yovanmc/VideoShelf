using System.Linq;
using Shouldly;
using VideoShelf.Core.Naming;

namespace VideoShelf.Core.Tests.Naming;

public class SectionGrouperTests
{
    [Fact]
    public void Groups_numbered_siblings_into_one_series_ordered_by_episode()
    {
        var files = new[]
        {
            "Cool Story.mp4",
            "Cool Story 2 the sequel.mp4",
            "Cool Story 3 finale.mp4",
            "Another Standalone Tale.mp4",
        };

        var result = SectionGrouper.Group(files);

        result.Series.Count.ShouldBe(2);

        var cool = result.Series.Single(s => s.BaseTitle == "Cool Story");
        cool.IsStandalone.ShouldBeFalse();
        cool.Episodes.Select(e => e.FileName)
            .ShouldBe(new[] { "Cool Story.mp4", "Cool Story 2 the sequel.mp4", "Cool Story 3 finale.mp4" });
        cool.Episodes.Select(e => e.EpisodeNumber).ShouldBe(new[] { 1, 2, 3 });

        var standalone = result.Series.Single(s => s.BaseTitle == "Another Standalone Tale");
        standalone.IsStandalone.ShouldBeTrue();
        standalone.Episodes.Count.ShouldBe(1);
    }

    [Fact]
    public void Single_numbered_file_with_no_siblings_is_a_standalone()
    {
        var result = SectionGrouper.Group(new[] { "Apollo 13.mkv" });
        var s = result.Series.Single();
        s.IsStandalone.ShouldBeTrue();
        s.Episodes.Single().FileName.ShouldBe("Apollo 13.mkv");
    }

    [Fact]
    public void Grouping_is_case_insensitive_on_base_title()
    {
        var result = SectionGrouper.Group(new[] { "skit.mp4", "SKIT 2.mp4" });
        result.Series.Count.ShouldBe(1);
        result.Series.Single().Episodes.Count.ShouldBe(2);
    }
}
