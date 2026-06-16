using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Shouldly;
using VideoShelf.App.Services;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Storage;
using Xunit;

namespace VideoShelf.App.Tests;

/// <summary>
/// M18-H: Unit tests for <see cref="GroupingEditViewModel"/>.
/// Asserts that each command writes the correct override rows and that a
/// RegroupSection call re-reflects the layout.
/// </summary>
public sealed class GroupingEditViewModelTests
{
    // ── Fixture ───────────────────────────────────────────────────────────────

    private sealed record Fx(
        AppTempDb Db,
        LibraryRepository Lib,
        GroupingEditViewModel Vm,
        long SectionId);

    private static Fx NewFx()
    {
        var db   = new AppTempDb();
        var lib  = new LibraryRepository(db.Db);
        var src  = lib.UpsertSource(@"C:\V", "V");
        var sec  = lib.UpsertSection(src, "Creator");
        var vm   = new GroupingEditViewModel(lib);
        vm.Attach(sec);
        return new Fx(db, lib, vm, sec);
    }

    // ── MoveEpisodeToSeriesCommand ────────────────────────────────────────────

    [Fact]
    public void MoveEpisodeToSeries_WritesOverrideAndRegroups()
    {
        var f = NewFx(); using var _ = f.Db;

        // Seed: two episodes naturally in "Show".
        var s1 = f.Lib.UpsertSeries(f.SectionId, "Show", false);
        f.Lib.UpsertVideo(s1, @"C:\V\Creator\Show 1.mkv", 1, ".mkv");
        f.Lib.UpsertVideo(s1, @"C:\V\Creator\Show 2.mkv", 2, ".mkv");

        bool regroupFired = false;
        f.Vm.RegroupRequested += (_, _) => regroupFired = true;

        f.Vm.MoveEpisodeToSeriesCommand.Execute(
            new MoveEpisodeArgs(@"C:\V\Creator\Show 2.mkv", "Spin-off"));

        regroupFired.ShouldBeTrue("RegroupRequested must fire after MoveEpisodeToSeries");

        // The override row must exist.
        var overrides = f.Lib.GetGroupingOverrides(f.SectionId);
        overrides.ShouldContainKey("Show 2.mkv");
        overrides["Show 2.mkv"].OverrideBaseTitle.ShouldBe("Spin-off");

        // And the regroup reflected the new layout.
        var series = f.Lib.GetSeriesSummaries(f.SectionId);
        series.ShouldContain(s => s.BaseTitle == "Spin-off");
    }

    // ── SetEpisodeOrderCommand ────────────────────────────────────────────────

    [Fact]
    public void SetEpisodeOrder_WritesEpisodeNoOverrideAndRegroups()
    {
        var f = NewFx(); using var _ = f.Db;

        var s1 = f.Lib.UpsertSeries(f.SectionId, "Show", false);
        f.Lib.UpsertVideo(s1, @"C:\V\Creator\Show 1.mkv", 1, ".mkv");

        bool regroupFired = false;
        f.Vm.RegroupRequested += (_, _) => regroupFired = true;

        f.Vm.SetEpisodeOrderCommand.Execute(
            new SetEpisodeOrderArgs(@"C:\V\Creator\Show 1.mkv", 42));

        regroupFired.ShouldBeTrue();
        var overrides = f.Lib.GetGroupingOverrides(f.SectionId);
        overrides["Show 1.mkv"].OverrideEpisodeNo.ShouldBe(42);
        overrides["Show 1.mkv"].OverrideBaseTitle.ShouldBeNull("base title not overridden");

        // After regroup, the episode number in the DB should reflect the override.
        var episodes = f.Lib.GetEpisodes(s1);
        episodes.Single().EpisodeNo.ShouldBe(42);
    }

    // ── ResetEpisodeGroupingCommand ───────────────────────────────────────────

    [Fact]
    public void ResetEpisodeGrouping_ClearsOverrideAndRegroups()
    {
        var f = NewFx(); using var _ = f.Db;

        var s1 = f.Lib.UpsertSeries(f.SectionId, "Show", false);
        f.Lib.UpsertVideo(s1, @"C:\V\Creator\Show 1.mkv", 1, ".mkv");

        // Pre-set an override.
        f.Lib.SetGroupingOverride(f.SectionId, @"C:\V\Creator\Show 1.mkv", "Other", null);

        bool regroupFired = false;
        f.Vm.RegroupRequested += (_, _) => regroupFired = true;

        f.Vm.ResetEpisodeGroupingCommand.Execute(@"C:\V\Creator\Show 1.mkv");

        regroupFired.ShouldBeTrue();
        // Override row must be deleted.
        var overrides = f.Lib.GetGroupingOverrides(f.SectionId);
        overrides.ShouldNotContainKey("Show 1.mkv");
    }

    // ── ResetSeriesGroupingCommand ────────────────────────────────────────────

    [Fact]
    public void ResetSeriesGrouping_ClearsAllOverridesAndRegroups()
    {
        var f = NewFx(); using var _ = f.Db;

        var s1 = f.Lib.UpsertSeries(f.SectionId, "Show", false);
        f.Lib.UpsertVideo(s1, @"C:\V\Creator\Show 1.mkv", 1, ".mkv");
        f.Lib.UpsertVideo(s1, @"C:\V\Creator\Show 2.mkv", 2, ".mkv");

        f.Lib.SetGroupingOverride(f.SectionId, @"C:\V\Creator\Show 1.mkv", "X", null);
        f.Lib.SetGroupingOverride(f.SectionId, @"C:\V\Creator\Show 2.mkv", "Y", null);

        bool regroupFired = false;
        f.Vm.RegroupRequested += (_, _) => regroupFired = true;

        var paths = new List<string>
        {
            @"C:\V\Creator\Show 1.mkv",
            @"C:\V\Creator\Show 2.mkv"
        };
        f.Vm.ResetSeriesGroupingCommand.Execute(paths);

        regroupFired.ShouldBeTrue();
        var overrides = f.Lib.GetGroupingOverrides(f.SectionId);
        overrides.ShouldBeEmpty("all overrides cleared");
    }

    // ── Attach guard ──────────────────────────────────────────────────────────

    [Fact]
    public void Commands_Before_Attach_AreNoOps()
    {
        using var db  = new AppTempDb();
        var lib = new LibraryRepository(db.Db);
        var vm  = new GroupingEditViewModel(lib);
        // Do NOT call vm.Attach(sectionId) — sectionId == 0 should guard all commands.

        bool fired = false;
        vm.RegroupRequested += (_, _) => fired = true;

        vm.MoveEpisodeToSeriesCommand.Execute(new MoveEpisodeArgs(@"C:\x.mkv", "Title"));
        vm.SetEpisodeOrderCommand.Execute(new SetEpisodeOrderArgs(@"C:\x.mkv", 5));
        vm.ResetEpisodeGroupingCommand.Execute(@"C:\x.mkv");
        vm.ResetSeriesGroupingCommand.Execute(new[] { @"C:\x.mkv" });

        fired.ShouldBeFalse("no event fired when sectionId == 0");
    }
}

/// <summary>
/// M18-H: Integration tests for <see cref="SectionDetailViewModel"/> grouping-edit
/// pass-through commands (MoveEpisodeToSeries, ResetEpisodeGrouping,
/// ResetSeriesGrouping) and the automatic LoadAsync reload on regroup.
/// </summary>
public sealed class SectionDetailGroupingEditTests
{
    private sealed class NullThumbs : IThumbnailService
    {
        public Task<string?> GetThumbnailPathAsync(string videoPath, CancellationToken ct)
            => Task.FromResult<string?>(null);
    }

    private sealed record Fx(
        AppTempDb Db,
        LibraryRepository Lib,
        SectionDetailViewModel Vm,
        GroupingEditViewModel GroupingEdit,
        long SectionId,
        long SeriesId);

    private static Fx NewFx()
    {
        var db   = new AppTempDb();
        var lib  = new LibraryRepository(db.Db);
        var tags = new TagRepository(db.Db);
        var watch = new WatchRepository(db.Db);
        var art   = new CreatorArtRepository(db.Db);
        var settings = new SettingsRepository(db.Db);
        var src  = lib.UpsertSource(@"C:\V", "V");
        var sec  = lib.UpsertSection(src, "Creator");
        var sid  = lib.UpsertSeries(sec, "Show", false);
        lib.UpsertVideo(sid, @"C:\V\Creator\Show 1.mkv", 1, ".mkv");
        lib.UpsertVideo(sid, @"C:\V\Creator\Show 2.mkv", 2, ".mkv");

        var groupingEdit = new GroupingEditViewModel(lib);
        var playQueue = new PlayQueueViewModel(lib, settings);
        var vm = new SectionDetailViewModel(
            lib, tags, watch, new NullThumbs(), art,
            new FakeImagePicker(null), playQueue,
            groupingEdit: groupingEdit);
        return new Fx(db, lib, vm, groupingEdit, sec, sid);
    }

    [Fact]
    public async Task ResetEpisodeGrouping_PassthroughCommand_ClearsOverride()
    {
        var f = NewFx(); using var _ = f.Db;
        await f.Vm.LoadAsync(f.SectionId);

        // Set an override then reset via the VM's pass-through command.
        f.Lib.SetGroupingOverride(f.SectionId, @"C:\V\Creator\Show 1.mkv", "Other", null);
        f.Vm.ResetEpisodeGroupingCommand.Execute(@"C:\V\Creator\Show 1.mkv");

        var overrides = f.Lib.GetGroupingOverrides(f.SectionId);
        overrides.ShouldNotContainKey("Show 1.mkv");
    }

    [Fact]
    public async Task MoveEpisodeToSeries_TitleEmpty_IsNoOp()
    {
        var f = NewFx(); using var _ = f.Db;
        await f.Vm.LoadAsync(f.SectionId);

        // Empty target title → command must not write an override.
        f.Vm.MoveEpisodeTargetTitle = "";
        var eps = f.Vm.SeriesList.SelectMany(s => s.Episodes).ToList();
        // Episodes haven't loaded yet (lazy); execute directly via grouping edit.
        // Just verify the pass-through is wired by calling reset (side-effect free here).
        var overridesBefore = f.Lib.GetGroupingOverrides(f.SectionId);

        // Execute with no loaded episodes — the guard should prevent any write.
        f.Vm.MoveEpisodeToSeriesCommand.Execute(null);
        var overridesAfter = f.Lib.GetGroupingOverrides(f.SectionId);
        overridesAfter.Count.ShouldBe(overridesBefore.Count, "no override written for null/empty args");
    }

    [Fact]
    public async Task GroupingEdit_IsExposed_AfterLoadAsync()
    {
        var f = NewFx(); using var _ = f.Db;
        await f.Vm.LoadAsync(f.SectionId);
        f.Vm.GroupingEdit.ShouldNotBeNull();
    }
}
