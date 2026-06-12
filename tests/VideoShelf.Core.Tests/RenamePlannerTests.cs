// tests/VideoShelf.Core.Tests/RenamePlannerTests.cs
using System.Collections.Generic;
using Shouldly;
using VideoShelf.Core.Models;
using VideoShelf.Core.Renaming;
using Xunit;

namespace VideoShelf.Core.Tests;

public class RenamePlannerTests
{
    private static Video V(long id, string path, int ep) =>
        new(id, 1, path, ep, System.IO.Path.GetFileName(path), "mkv", null, null, false, "", false);

    [Fact]
    public void Ready_WhenTargetIsFreeAndSourceExists()
    {
        var fs = new InMemoryFileSystem(@"C:\lib\old1.mkv");
        var planner = new RenamePlanner(fs);
        var videos = new[] { V(1, @"C:\lib\old1.mkv", 1) };
        var proposed = new Dictionary<long, string> { [1] = "Show 01.mkv" };

        var plan = planner.BuildPlan(videos, proposed);

        plan.Items[0].Status.ShouldBe(RenameItemStatus.Ready);
        plan.Items[0].NewName.ShouldBe("Show 01.mkv");
        plan.ReadyCount.ShouldBe(1);
    }

    [Fact]
    public void Unchanged_WhenProposedEqualsCurrent()
    {
        var fs = new InMemoryFileSystem(@"C:\lib\Show 01.mkv");
        var plan = new RenamePlanner(fs).BuildPlan(
            new[] { V(1, @"C:\lib\Show 01.mkv", 1) },
            new Dictionary<long, string> { [1] = "Show 01.mkv" });
        plan.Items[0].Status.ShouldBe(RenameItemStatus.Unchanged);
    }

    [Fact]
    public void TargetExists_WhenADifferentFileOccupiesTheName()
    {
        var fs = new InMemoryFileSystem(@"C:\lib\old1.mkv", @"C:\lib\Show 01.mkv");
        var plan = new RenamePlanner(fs).BuildPlan(
            new[] { V(1, @"C:\lib\old1.mkv", 1) },
            new Dictionary<long, string> { [1] = "Show 01.mkv" });
        plan.Items[0].Status.ShouldBe(RenameItemStatus.TargetExists);
    }

    [Fact]
    public void DuplicateTarget_WhenTwoRowsMapToTheSameName()
    {
        var fs = new InMemoryFileSystem(@"C:\lib\a.mkv", @"C:\lib\b.mkv");
        var plan = new RenamePlanner(fs).BuildPlan(
            new[] { V(1, @"C:\lib\a.mkv", 1), V(2, @"C:\lib\b.mkv", 1) },
            new Dictionary<long, string> { [1] = "Show 01.mkv", [2] = "Show 01.mkv" });
        plan.Items[0].Status.ShouldBe(RenameItemStatus.DuplicateTarget);
        plan.Items[1].Status.ShouldBe(RenameItemStatus.DuplicateTarget);
    }

    [Fact]
    public void SourceMissing_WhenFileNotOnDisk()
    {
        var fs = new InMemoryFileSystem(); // empty
        var plan = new RenamePlanner(fs).BuildPlan(
            new[] { V(1, @"C:\lib\gone.mkv", 1) },
            new Dictionary<long, string> { [1] = "Show 01.mkv" });
        plan.Items[0].Status.ShouldBe(RenameItemStatus.SourceMissing);
    }

    [Fact]
    public void InvalidName_WhenProposedHasIllegalCharacters()
    {
        var fs = new InMemoryFileSystem(@"C:\lib\a.mkv");
        var plan = new RenamePlanner(fs).BuildPlan(
            new[] { V(1, @"C:\lib\a.mkv", 1) },
            new Dictionary<long, string> { [1] = "bad/name.mkv" });
        plan.Items[0].Status.ShouldBe(RenameItemStatus.InvalidName);
    }
}
