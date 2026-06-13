using Shouldly;
using VideoShelf.Core.Playback;

namespace VideoShelf.Core.Tests.Playback;

public class SubtitleSidecarsTests
{
    [Fact]
    public void ExactBaseMatch_ReturnsSrtSibling()
    {
        var result = SubtitleSidecars.Find(
            @"C:\m\movie.mkv",
            new[] { @"C:\m\movie.srt", @"C:\m\movie.mkv" });

        result.ShouldBe(new[] { @"C:\m\movie.srt" });
    }

    [Fact]
    public void LanguageTagged_ReturnsBothSidecars()
    {
        var result = SubtitleSidecars.Find(
            @"C:\m\movie.mkv",
            new[] { @"C:\m\movie.en.srt", @"C:\m\movie.fr.ass", @"C:\m\movie.mkv" });

        result.Count.ShouldBe(2);
        result.ShouldContain(@"C:\m\movie.en.srt");
        result.ShouldContain(@"C:\m\movie.fr.ass");
    }

    [Fact]
    public void ExcludesNonSubtitleSiblingsAndVideoItself()
    {
        var result = SubtitleSidecars.Find(
            @"C:\m\movie.mkv",
            new[] { @"C:\m\movie.jpg", @"C:\m\other.srt", @"C:\m\movie.mkv" });

        result.ShouldBeEmpty();
    }

    [Fact]
    public void CaseInsensitiveExtension_Matches()
    {
        var result = SubtitleSidecars.Find(
            @"C:\m\movie.mkv",
            new[] { @"C:\m\MOVIE.SRT" });

        result.ShouldBe(new[] { @"C:\m\MOVIE.SRT" });
    }
}
