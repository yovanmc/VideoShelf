using VideoShelf.App.Harness;
using Xunit;

namespace VideoShelf.App.Tests.Harness;

public class HarnessOptionsTests
{
    [Fact]
    public void Parse_Empty_IsNotHarness()
    {
        var o = HarnessOptions.Parse(System.Array.Empty<string>());
        Assert.False(o.IsHarness);
        Assert.Null(o.Folder);
        Assert.Equal("Home", o.View);
        Assert.False(o.AutoStart);
        Assert.False(o.SeedDemo);
    }

    [Fact]
    public void Parse_KeyValuePairs_AreCaptured()
    {
        var o = HarnessOptions.Parse(new[]
        {
            "--folder", @"C:\fix", "--data-dir", @"C:\data",
            "--view", "Player", "--play", @"C:\fix\a.mp4",
            "--done-signal", @"C:\sig.txt"
        });
        Assert.Equal(@"C:\fix", o.Folder);
        Assert.Equal(@"C:\data", o.DataDir);
        Assert.Equal("Player", o.View);
        Assert.Equal(@"C:\fix\a.mp4", o.Play);
        Assert.Equal(@"C:\sig.txt", o.DoneSignal);
        Assert.True(o.IsHarness);
    }

    [Fact]
    public void Parse_BooleanFlags_NeedNoValue()
    {
        var o = HarnessOptions.Parse(new[] { "--autostart", "--seed-demo", "--folder", @"C:\fix" });
        Assert.True(o.AutoStart);
        Assert.True(o.SeedDemo);
        Assert.Equal(@"C:\fix", o.Folder);
    }

    [Fact]
    public void Parse_UnknownArgs_AreIgnored()
    {
        var o = HarnessOptions.Parse(new[] { "--bogus", "x", "--view", "Browse" });
        Assert.Equal("Browse", o.View);
    }

    [Fact]
    public void Parse_FlagsAreCaseInsensitive()
    {
        var o = HarnessOptions.Parse(new[] { "--FOLDER", @"C:\fix", "--AutoStart" });
        Assert.Equal(@"C:\fix", o.Folder);
        Assert.True(o.AutoStart);
    }

    [Fact]
    public void IsHarness_TrueWhenDoneSignalOnly()
    {
        var o = HarnessOptions.Parse(new[] { "--done-signal", @"C:\s.txt" });
        Assert.True(o.IsHarness);
    }
}
