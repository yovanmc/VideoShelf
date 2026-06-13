// tests/VideoShelf.Core.Tests/RenamePlannerMultiSeriesTests.cs
// H3 — RenamePlanner over a multi-series video set: cross-series target collisions,
// unchanged / ready / source-missing statuses.
using System.Collections.Generic;
using Shouldly;
using VideoShelf.Core.Models;
using VideoShelf.Core.Renaming;
using Xunit;

namespace VideoShelf.Core.Tests;

public class RenamePlannerMultiSeriesTests
{
    // Helper: build a minimal Video record with the fields RenamePlanner needs.
    // Video(Id, SeriesId, FilePath, EpisodeNo, RawFilename, Format, Duration, ThumbnailPath, Watched, AddedAt, Missing)
    private static Video V(long id, string filePath, int episode = 1) =>
        new Video(id, 0, filePath, episode, "", "mkv", null, null, false, "", false);

    [Fact]
    public void BuildPlan_MultiSeriesReadyItems_AllFlaggedReady()
    {
        var fs = new InMemoryFileSystem(
            @"C:\A\Show A 01.mkv",
            @"C:\B\Show B 01.mkv");
        var planner = new RenamePlanner(fs);

        var videos = new List<Video>
        {
            V(1, @"C:\A\Show A 01.mkv"),
            V(2, @"C:\B\Show B 01.mkv"),
        };
        var proposed = new Dictionary<long, string>
        {
            [1] = "Renamed A 01.mkv",
            [2] = "Renamed B 01.mkv",
        };

        var plan = planner.BuildPlan(videos, proposed);

        plan.Items.Count.ShouldBe(2);
        plan.Items[0].Status.ShouldBe(RenameItemStatus.Ready);
        plan.Items[1].Status.ShouldBe(RenameItemStatus.Ready);
    }

    [Fact]
    public void BuildPlan_CrossSeriesTargetCollision_FlaggedDuplicateTarget()
    {
        // Two videos in DIFFERENT directories; both try to rename to the same filename,
        // but in their own directories — those are NOT the same target path, so no collision.
        // However if they share a directory and propose the same name, that IS a collision.
        var fs = new InMemoryFileSystem(
            @"C:\shared\old1.mkv",
            @"C:\shared\old2.mkv");
        var planner = new RenamePlanner(fs);

        var videos = new List<Video>
        {
            V(1, @"C:\shared\old1.mkv", episode: 1),
            V(2, @"C:\shared\old2.mkv", episode: 2),
        };
        // Both propose the SAME target path — classic DuplicateTarget.
        var proposed = new Dictionary<long, string>
        {
            [1] = "Same Name 01.mkv",
            [2] = "Same Name 01.mkv",   // duplicate!
        };

        var plan = planner.BuildPlan(videos, proposed);

        plan.Items[0].Status.ShouldBe(RenameItemStatus.DuplicateTarget);
        plan.Items[1].Status.ShouldBe(RenameItemStatus.DuplicateTarget);
    }

    [Fact]
    public void BuildPlan_CrossSeriesDifferentDirectories_SameFilenameNotACollision()
    {
        // Same proposed filename but in DIFFERENT directories → different absolute paths → Ready.
        var fs = new InMemoryFileSystem(
            @"C:\A\old1.mkv",
            @"C:\B\old2.mkv");
        var planner = new RenamePlanner(fs);

        var videos = new List<Video>
        {
            V(1, @"C:\A\old1.mkv"),
            V(2, @"C:\B\old2.mkv"),
        };
        var proposed = new Dictionary<long, string>
        {
            [1] = "Episode 01.mkv",
            [2] = "Episode 01.mkv",  // same name but different dirs → different paths
        };

        var plan = planner.BuildPlan(videos, proposed);

        plan.Items[0].Status.ShouldBe(RenameItemStatus.Ready);
        plan.Items[1].Status.ShouldBe(RenameItemStatus.Ready);
    }

    [Fact]
    public void BuildPlan_SourceMissing_FlaggedCorrectly()
    {
        // File not in the InMemoryFileSystem → SourceMissing.
        var fs = new InMemoryFileSystem(); // empty — no files on "disk"
        var planner = new RenamePlanner(fs);

        var videos = new List<Video> { V(1, @"C:\A\old.mkv") };
        var proposed = new Dictionary<long, string> { [1] = "New 01.mkv" };

        var plan = planner.BuildPlan(videos, proposed);

        plan.Items[0].Status.ShouldBe(RenameItemStatus.SourceMissing);
    }

    [Fact]
    public void BuildPlan_UnchangedName_FlaggedUnchanged()
    {
        var fs = new InMemoryFileSystem(@"C:\A\Show 01.mkv");
        var planner = new RenamePlanner(fs);

        var videos = new List<Video> { V(1, @"C:\A\Show 01.mkv") };
        var proposed = new Dictionary<long, string> { [1] = "Show 01.mkv" }; // same name

        var plan = planner.BuildPlan(videos, proposed);

        plan.Items[0].Status.ShouldBe(RenameItemStatus.Unchanged);
    }

    [Fact]
    public void BuildPlan_MixedStatuses_ReturnsCorrectPerRow()
    {
        var fs = new InMemoryFileSystem(
            @"C:\A\a.mkv",
            @"C:\B\b.mkv");
        var planner = new RenamePlanner(fs);

        var videos = new List<Video>
        {
            V(1, @"C:\A\a.mkv"),
            V(2, @"C:\B\b.mkv"),
            V(3, @"C:\C\missing.mkv"), // not in FS
        };
        var proposed = new Dictionary<long, string>
        {
            [1] = "A New 01.mkv",   // Ready
            [2] = "b.mkv",          // Unchanged (same name, same dir)
            [3] = "C New 01.mkv",   // SourceMissing
        };

        var plan = planner.BuildPlan(videos, proposed);

        plan.Items[0].Status.ShouldBe(RenameItemStatus.Ready);
        plan.Items[1].Status.ShouldBe(RenameItemStatus.Unchanged);
        plan.Items[2].Status.ShouldBe(RenameItemStatus.SourceMissing);
    }
}
