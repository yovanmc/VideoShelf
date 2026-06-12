// tests/VideoShelf.Core.Tests/InMemoryFileSystemTests.cs
using System.IO;
using Shouldly;
using VideoShelf.Core.Tests;
using Xunit;

namespace VideoShelf.Core.Tests;

public class InMemoryFileSystemTests
{
    [Fact]
    public void Move_RelocatesFile_AndThrowsOnExistingTarget()
    {
        var fs = new InMemoryFileSystem(@"C:\lib\a.mkv");
        fs.Move(@"C:\lib\a.mkv", @"C:\lib\b.mkv");
        fs.FileExists(@"C:\lib\a.mkv").ShouldBeFalse();
        fs.FileExists(@"C:\lib\b.mkv").ShouldBeTrue();

        fs.AddFile(@"C:\lib\c.mkv");
        Should.Throw<IOException>(() => fs.Move(@"C:\lib\b.mkv", @"C:\lib\c.mkv"));
    }
}
