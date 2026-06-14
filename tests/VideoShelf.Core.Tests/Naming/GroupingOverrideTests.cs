using System.Collections.Generic;
using System.Linq;
using Shouldly;
using VideoShelf.Core.Naming;
using VideoShelf.Core.Scanning;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;

namespace VideoShelf.Core.Tests.Naming;

/// <summary>
/// Tests for the <see cref="GroupingOverride"/> overload of <see cref="SectionGrouper.Group"/>
/// and for the supporting <see cref="LibraryRepository"/> CRUD methods.
/// </summary>
public class GroupingOverrideTests
{
    // ── B1: SectionGrouper.Group overload ─────────────────────────────────────

    [Fact]
    public void Split_routes_one_file_into_a_new_series()
    {
        // Two files that the parser would naturally group together (same base title "Cool Story").
        var files = new[] { "Cool Story.mp4", "Cool Story 2.mp4" };

        // Override the first file to belong to a brand-new series "Other Show".
        var overrides = new Dictionary<string, GroupingOverride>(System.StringComparer.OrdinalIgnoreCase)
        {
            ["Cool Story.mp4"] = new GroupingOverride("Cool Story.mp4", "Other Show", null),
        };

        var result = SectionGrouper.Group(files, overrides);

        // Should produce two series: "Other Show" (split-out) and "Cool Story" (the remaining).
        result.Series.Count.ShouldBe(2);

        var other = result.Series.Single(s => s.BaseTitle.Equals("Other Show", System.StringComparison.OrdinalIgnoreCase));
        other.IsStandalone.ShouldBeTrue();
        other.Episodes.Single().FileName.ShouldBe("Cool Story.mp4");

        var cool = result.Series.Single(s => s.BaseTitle.Equals("Cool Story", System.StringComparison.OrdinalIgnoreCase));
        cool.IsStandalone.ShouldBeTrue();
        cool.Episodes.Single().FileName.ShouldBe("Cool Story 2.mp4");
    }

    [Fact]
    public void Merge_folds_two_series_into_one()
    {
        // Parser would produce two series: "Alpha Show" and "Beta Show".
        var files = new[] { "Alpha Show.mp4", "Alpha Show 2.mp4", "Beta Show.mp4" };

        // Override "Beta Show.mp4" to use base title "Alpha Show" → merge into Alpha.
        var overrides = new Dictionary<string, GroupingOverride>(System.StringComparer.OrdinalIgnoreCase)
        {
            ["Beta Show.mp4"] = new GroupingOverride("Beta Show.mp4", "Alpha Show", 3),
        };

        var result = SectionGrouper.Group(files, overrides);

        // One merged series with three episodes.
        result.Series.Count.ShouldBe(1);
        var merged = result.Series.Single();
        merged.BaseTitle.ShouldBe("Alpha Show");
        merged.Episodes.Count.ShouldBe(3);
        // Episode numbers: Alpha Show→1, Alpha Show 2→2, Beta Show (override ep=3)→3.
        merged.Episodes.Select(e => e.EpisodeNumber).ShouldBe(new[] { 1, 2, 3 });
        merged.Episodes.Select(e => e.FileName).ShouldContain("Beta Show.mp4");
    }

    [Fact]
    public void Manual_episode_no_override_reorders_episodes()
    {
        // Two files: parser gives ep1 and ep2; we swap their order via overrides.
        var files = new[] { "Show Episode 1.mp4", "Show Episode 2.mp4" };

        var overrides = new Dictionary<string, GroupingOverride>(System.StringComparer.OrdinalIgnoreCase)
        {
            ["Show Episode 1.mp4"] = new GroupingOverride("Show Episode 1.mp4", null, 10),
            ["Show Episode 2.mp4"] = new GroupingOverride("Show Episode 2.mp4", null, 5),
        };

        var result = SectionGrouper.Group(files, overrides);

        result.Series.Count.ShouldBe(1);
        var series = result.Series.Single();
        series.Episodes.Count.ShouldBe(2);
        // Should be ordered by override episode number: 5 then 10.
        series.Episodes[0].EpisodeNumber.ShouldBe(5);
        series.Episodes[0].FileName.ShouldBe("Show Episode 2.mp4");
        series.Episodes[1].EpisodeNumber.ShouldBe(10);
        series.Episodes[1].FileName.ShouldBe("Show Episode 1.mp4");
    }

    [Fact]
    public void Re_running_Group_with_same_dict_is_stable()
    {
        var files = new[] { "Cool Story.mp4", "Cool Story 2.mp4", "Beta Show.mp4" };
        var overrides = new Dictionary<string, GroupingOverride>(System.StringComparer.OrdinalIgnoreCase)
        {
            ["Beta Show.mp4"] = new GroupingOverride("Beta Show.mp4", "Cool Story", 3),
        };

        var result1 = SectionGrouper.Group(files, overrides);
        var result2 = SectionGrouper.Group(files, overrides);

        result1.Series.Count.ShouldBe(result2.Series.Count);
        for (var i = 0; i < result1.Series.Count; i++)
        {
            result1.Series[i].BaseTitle.ShouldBe(result2.Series[i].BaseTitle);
            result1.Series[i].Episodes.Count.ShouldBe(result2.Series[i].Episodes.Count);
        }
    }

    [Fact]
    public void Empty_override_dict_produces_same_result_as_no_arg_overload()
    {
        var files = new[] { "Show.mp4", "Show 2.mp4", "Standalone.mp4" };

        var noOverride = SectionGrouper.Group(files);
        var emptyOverride = SectionGrouper.Group(files,
            new Dictionary<string, GroupingOverride>(System.StringComparer.OrdinalIgnoreCase));

        noOverride.Series.Count.ShouldBe(emptyOverride.Series.Count);
    }

    // ── B2: LibraryRepository CRUD for grouping overrides ─────────────────────

    [Fact]
    public void GetGroupingOverrides_returns_empty_when_no_rows_exist()
    {
        using var temp = new TempDb();
        var lib = new LibraryRepository(temp.Db);
        var sourceId = lib.UpsertSource(@"C:\Videos", "My Videos");
        var sectionId = lib.UpsertSection(sourceId, "Creator A");

        var overrides = lib.GetGroupingOverrides(sectionId);

        overrides.ShouldBeEmpty();
    }

    [Fact]
    public void SetGroupingOverride_upserts_and_GetGroupingOverrides_returns_it_keyed_by_bare_filename()
    {
        using var temp = new TempDb();
        var lib = new LibraryRepository(temp.Db);
        var sourceId = lib.UpsertSource(@"C:\Videos", "My Videos");
        var sectionId = lib.UpsertSection(sourceId, "Creator A");

        var fullPath = @"C:\Videos\Creator A\Cool Story.mp4";
        lib.SetGroupingOverride(sectionId, fullPath, "Other Show", 5);

        var overrides = lib.GetGroupingOverrides(sectionId);

        overrides.Count.ShouldBe(1);
        overrides.ContainsKey("Cool Story.mp4").ShouldBeTrue();
        var ov = overrides["Cool Story.mp4"];
        ov.OverrideBaseTitle.ShouldBe("Other Show");
        ov.OverrideEpisodeNo.ShouldBe(5);
        ov.FilePath.ShouldBe(fullPath);
    }

    [Fact]
    public void SetGroupingOverride_updates_existing_row_on_conflict()
    {
        using var temp = new TempDb();
        var lib = new LibraryRepository(temp.Db);
        var sourceId = lib.UpsertSource(@"C:\Videos", "My Videos");
        var sectionId = lib.UpsertSection(sourceId, "Creator A");
        var fullPath = @"C:\Videos\Creator A\Cool Story.mp4";

        lib.SetGroupingOverride(sectionId, fullPath, "First Title", null);
        lib.SetGroupingOverride(sectionId, fullPath, "Updated Title", 7);

        var overrides = lib.GetGroupingOverrides(sectionId);
        overrides.Count.ShouldBe(1);
        overrides["Cool Story.mp4"].OverrideBaseTitle.ShouldBe("Updated Title");
        overrides["Cool Story.mp4"].OverrideEpisodeNo.ShouldBe(7);
    }

    [Fact]
    public void ClearGroupingOverride_removes_the_row()
    {
        using var temp = new TempDb();
        var lib = new LibraryRepository(temp.Db);
        var sourceId = lib.UpsertSource(@"C:\Videos", "My Videos");
        var sectionId = lib.UpsertSection(sourceId, "Creator A");
        var fullPath = @"C:\Videos\Creator A\Cool Story.mp4";

        lib.SetGroupingOverride(sectionId, fullPath, "Other Show", null);
        lib.ClearGroupingOverride(sectionId, fullPath);

        var overrides = lib.GetGroupingOverrides(sectionId);
        overrides.ShouldBeEmpty();
    }

    [Fact]
    public void ClearGroupingOverride_is_idempotent_when_row_does_not_exist()
    {
        using var temp = new TempDb();
        var lib = new LibraryRepository(temp.Db);
        var sourceId = lib.UpsertSource(@"C:\Videos", "My Videos");
        var sectionId = lib.UpsertSection(sourceId, "Creator A");

        // Should not throw even if the row was never inserted.
        Should.NotThrow(() =>
            lib.ClearGroupingOverride(sectionId, @"C:\Videos\Creator A\Nonexistent.mp4"));
    }

    [Fact]
    public void ScanSource_applies_override_so_rescan_produces_same_grouping()
    {
        // End-to-end: insert an override BEFORE the scan; verify the scan picks it up.
        using var temp = new TempDb();
        using var dir = new TempDir();
        dir.Touch("Creator A/Cool Story.mp4");
        dir.Touch("Creator A/Cool Story 2.mp4");

        var lib = new LibraryRepository(temp.Db);
        var scan = new ScanService(temp.Db, lib);

        // First scan (no overrides) → Cool Story groups as one series with 2 episodes.
        scan.ScanSource(dir.Path, "My Videos");
        var sourceId = lib.GetSources().Single().Id;
        var sectionId = lib.GetSections(sourceId).Single().Id;
        lib.GetSeriesForSection(sectionId).Count.ShouldBe(1);

        // Insert an override that splits "Cool Story.mp4" into "Other Show".
        var fullPath = System.IO.Path.Combine(dir.Path, "Creator A", "Cool Story.mp4");
        lib.SetGroupingOverride(sectionId, fullPath, "Other Show", null);

        // Rescan: the override should route Cool Story.mp4 into "Other Show".
        scan.ScanSource(dir.Path, "My Videos");
        var series = lib.GetSeriesForSection(sectionId);
        series.Count.ShouldBe(2);
        series.ShouldContain(s => s.BaseTitle.Equals("Other Show", System.StringComparison.OrdinalIgnoreCase));
        series.ShouldContain(s => s.BaseTitle.Equals("Cool Story", System.StringComparison.OrdinalIgnoreCase));
    }
}
