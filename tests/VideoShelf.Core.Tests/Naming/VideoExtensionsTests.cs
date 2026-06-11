using Shouldly;
using VideoShelf.Core.Naming;

namespace VideoShelf.Core.Tests.Naming;

public class VideoExtensionsTests
{
    [Theory]
    [InlineData("movie.mp4", true)]
    [InlineData("clip.MKV", true)]          // case-insensitive
    [InlineData("show.mov", true)]
    [InlineData("a.webm", true)]
    [InlineData("notes.txt", false)]
    [InlineData("poster.jpg", false)]
    [InlineData("noext", false)]
    public void IsVideo_matches_known_extensions(string fileName, bool expected)
        => VideoExtensions.IsVideo(fileName).ShouldBe(expected);
}
