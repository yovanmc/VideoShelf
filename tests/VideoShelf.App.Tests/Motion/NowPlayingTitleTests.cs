// tests/VideoShelf.App.Tests/Motion/NowPlayingTitleTests.cs
using Xunit; using Shouldly;
public class NowPlayingTitleTests
{
    [Theory]
    [InlineData("", "VideoShelf")]
    [InlineData("Big Buck Bunny", "Big Buck Bunny — VideoShelf")]
    public void WindowTitle_composes(string nowPlaying, string expected)
        => VideoShelf.App.ViewModels.MainViewModel.ComposeWindowTitle(nowPlaying).ShouldBe(expected);
}
