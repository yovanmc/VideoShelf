using VideoShelf.App.Accessibility;
using Xunit;

namespace VideoShelf.App.Tests.Accessibility;

public class LiveRegionTests
{
    [Theory]
    [InlineData(null, "Scanning…", true)]
    [InlineData("Scanning…", "Scanning…", false)]
    [InlineData("Scanning…", "Done", true)]
    [InlineData("Done", "", false)]
    [InlineData(null, "", false)]
    public void ShouldAnnounce(string? oldText, string newText, bool expected)
        => Assert.Equal(expected, LiveRegion.ShouldAnnounce(oldText, newText));
}
