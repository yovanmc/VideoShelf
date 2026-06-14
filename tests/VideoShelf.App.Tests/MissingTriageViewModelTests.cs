using System;
using System.IO;
using Shouldly;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Storage;
using CoreTempDir = VideoShelf.Core.Tests.TestSupport.TempDir;

namespace VideoShelf.App.Tests;

/// <summary>
/// M18-F unit tests for <see cref="MissingTriageViewModel"/>.
/// Covers: relink calls RelinkVideo; remove-series calls DeleteSeriesIndex;
/// remove-creator calls DeleteSectionIndex; auto-find by size_bytes.
/// </summary>
public sealed class MissingTriageViewModelTests
{
    // ── Fixture ───────────────────────────────────────────────────────────────

    private sealed record Fx(
        AppTempDb TempDb,
        LibraryRepository Lib,
        MaintenanceRepository Maintenance,
        FakeVideoFilePicker Picker,
        FakeConfirmService Confirm,
        MissingTriageViewModel Vm);

    private static Fx NewFx()
    {
        var db    = new AppTempDb();
        var lib   = new LibraryRepository(db.Db);
        var maint = new MaintenanceRepository(db.Db);
        var pick  = new FakeVideoFilePicker();
        var conf  = new FakeConfirmService();
        var vm    = new MissingTriageViewModel(maint, lib, pick, conf);
        return new Fx(db, lib, maint, pick, conf, vm);
    }

    /// <summary>Seed a source → section → series → video; mark all missing.</summary>
    private static (long srcId, long secId, long seriesId, long videoId) SeedMissingVideo(
        LibraryRepository lib, string creatorName = "Creator", string seriesTitle = "Show")
    {
        var srcId    = lib.UpsertSource(@"C:\V", "V");
        var secId    = lib.UpsertSection(srcId, creatorName);
        var seriesId = lib.UpsertSeries(secId, seriesTitle, false);
        var videoId  = lib.UpsertVideo(seriesId, $@"C:\V\{creatorName}\video.mp4", 1, ".mp4");
        lib.MarkAllMissingForSource(srcId);
        return (srcId, secId, seriesId, videoId);
    }

    // ── Load ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Load_populates_MissingVideos_from_db()
    {
        var f = NewFx(); using var _ = f.TempDb;

        SeedMissingVideo(f.Lib);
        f.Vm.Load();

        f.Vm.MissingVideos.Count.ShouldBe(1);
        f.Vm.MissingVideos[0].CreatorName.ShouldBe("Creator");
    }

    [Fact]
    public void Load_populates_OrphanSeries_and_EmptyCreators()
    {
        var f = NewFx(); using var _ = f.TempDb;

        var (_, _, seriesId, _) = SeedMissingVideo(f.Lib, "Creator1", "Show1");
        // After marking missing, Creator1's Show1 becomes orphan + Creator1 becomes empty.

        f.Vm.Load();

        f.Vm.OrphanSeries.Count.ShouldBeGreaterThan(0);
        f.Vm.EmptyCreators.Count.ShouldBeGreaterThan(0);
    }

    // ── Relink (manual) ───────────────────────────────────────────────────────

    [Fact]
    public void RelinkManual_calls_RelinkVideo_and_removes_row()
    {
        var f = NewFx(); using var _ = f.TempDb;

        var (_, _, _, videoId) = SeedMissingVideo(f.Lib);
        f.Vm.Load();
        f.Vm.MissingVideos.Count.ShouldBe(1);

        // Create a real temp file so File.Exists passes.
        var newPath = Path.Combine(Path.GetTempPath(), "vshelf_relink_" + Guid.NewGuid().ToString("N") + ".mp4");
        File.WriteAllBytes(newPath, new byte[10]);
        try
        {
            f.Picker.NextResult = newPath;
            f.Vm.MissingVideos[0].RelinkCommand.Execute(null);

            // Row removed from the list.
            f.Vm.MissingVideos.Count.ShouldBe(0);

            // DB: video should no longer be missing.
            var remaining = f.Maintenance.GetMissingVideos();
            remaining.ShouldBeEmpty();
        }
        finally
        {
            File.Delete(newPath);
        }
    }

    [Fact]
    public void RelinkManual_does_nothing_when_picker_cancelled()
    {
        var f = NewFx(); using var _ = f.TempDb;

        SeedMissingVideo(f.Lib);
        f.Vm.Load();

        f.Picker.NextResult = null; // cancelled
        f.Vm.MissingVideos[0].RelinkCommand.Execute(null);

        f.Vm.MissingVideos.Count.ShouldBe(1); // unchanged
    }

    [Fact]
    public void RelinkManual_sets_error_when_picked_file_does_not_exist()
    {
        var f = NewFx(); using var _ = f.TempDb;

        SeedMissingVideo(f.Lib);
        f.Vm.Load();

        f.Picker.NextResult = @"C:\NonExistent\video.mp4"; // won't exist
        f.Vm.MissingVideos[0].RelinkCommand.Execute(null);

        f.Vm.StatusMessage.ShouldContain("not found");
        f.Vm.MissingVideos.Count.ShouldBe(1); // unchanged
    }

    // ── Auto-find by size_bytes ───────────────────────────────────────────────

    [Fact]
    public void AutoFind_relinks_when_unique_size_match_found_in_source_root()
    {
        var f = NewFx(); using var _ = f.TempDb;

        // Use an actual temp directory as the source root so we can place a real file in it.
        using var tempDir = new CoreTempDir();
        var srcId    = f.Lib.UpsertSource(tempDir.Path, "AutoSrc");
        var secId    = f.Lib.UpsertSection(srcId, "Creator");
        var seriesId = f.Lib.UpsertSeries(secId, "Show", false);

        // Place a real file in the temp dir so the directory walk finds it.
        var candidatePath = Path.Combine(tempDir.Path, "moved_video.mp4");
        var fileSize = 2048L;
        File.WriteAllBytes(candidatePath, new byte[fileSize]);

        // Register a video with a DIFFERENT (missing) path but same size so auto-find matches it.
        var oldPath = Path.Combine(tempDir.Path, "original_video.mp4");
        var videoId = f.Lib.UpsertVideo(seriesId, oldPath, 1, ".mp4", sizeBytes: fileSize);
        f.Lib.MarkAllMissingForSource(srcId);

        f.Vm.Load();
        f.Vm.MissingVideos.Count.ShouldBe(1);

        f.Vm.MissingVideos[0].AutoFindCommand.Execute(null);

        // Row removed — auto-find succeeded without the picker.
        f.Vm.MissingVideos.Count.ShouldBe(0);
        f.Maintenance.GetMissingVideos().ShouldBeEmpty();
    }

    [Fact]
    public void AutoFind_falls_back_to_picker_when_no_unique_match()
    {
        var f = NewFx(); using var _ = f.TempDb;

        using var tempDir = new CoreTempDir();
        var srcId    = f.Lib.UpsertSource(tempDir.Path, "AutoSrc");
        var secId    = f.Lib.UpsertSection(srcId, "Creator");
        var seriesId = f.Lib.UpsertSeries(secId, "Show", false);

        // Two files with same size → ambiguous, auto-find returns null → falls back to picker.
        var size = 1024L;
        File.WriteAllBytes(Path.Combine(tempDir.Path, "a.mp4"), new byte[size]);
        File.WriteAllBytes(Path.Combine(tempDir.Path, "b.mp4"), new byte[size]);

        var oldPath = Path.Combine(tempDir.Path, "old.mp4");
        f.Lib.UpsertVideo(seriesId, oldPath, 1, ".mp4", sizeBytes: size);
        f.Lib.MarkAllMissingForSource(srcId);

        f.Vm.Load();

        // Make the picker also cancel so we can observe the "tried but no match" message.
        f.Picker.NextResult = null;
        f.Vm.MissingVideos[0].AutoFindCommand.Execute(null);

        // Row unchanged (picker cancelled).
        f.Vm.MissingVideos.Count.ShouldBe(1);
        // Status message indicates failure.
        f.Vm.StatusMessage.ShouldContain("no unique match");
    }

    // ── Orphan series removal ─────────────────────────────────────────────────

    [Fact]
    public void RemoveOrphanSeries_calls_DeleteSeriesIndex_and_removes_row()
    {
        var f = NewFx(); using var _ = f.TempDb;

        var (_, _, seriesId, _) = SeedMissingVideo(f.Lib);
        f.Vm.Load();
        var orphanRow = f.Vm.OrphanSeries.ShouldHaveSingleItem();
        orphanRow.SeriesId.ShouldBe(seriesId);

        f.Confirm.NextResult = true;
        orphanRow.RemoveFromLibraryCommand.Execute(null);

        f.Vm.OrphanSeries.ShouldBeEmpty();
        // The series should be gone from the DB.
        f.Maintenance.GetOrphanSeries().ShouldBeEmpty();
    }

    [Fact]
    public void RemoveOrphanSeries_does_nothing_when_confirm_cancelled()
    {
        var f = NewFx(); using var _ = f.TempDb;

        SeedMissingVideo(f.Lib);
        f.Vm.Load();

        f.Confirm.NextResult = false;
        f.Vm.OrphanSeries[0].RemoveFromLibraryCommand.Execute(null);

        f.Vm.OrphanSeries.Count.ShouldBe(1); // unchanged
    }

    // ── Empty creator removal ─────────────────────────────────────────────────

    [Fact]
    public void RemoveEmptyCreator_calls_DeleteSectionIndex_and_removes_row()
    {
        var f = NewFx(); using var _ = f.TempDb;

        var (_, secId, _, _) = SeedMissingVideo(f.Lib, "Empty Creator");
        f.Vm.Load();
        var creatorRow = f.Vm.EmptyCreators.ShouldHaveSingleItem();
        creatorRow.SectionId.ShouldBe(secId);

        f.Confirm.NextResult = true;
        creatorRow.RemoveFromLibraryCommand.Execute(null);

        f.Vm.EmptyCreators.ShouldBeEmpty();
        f.Maintenance.GetEmptyCreators().ShouldBeEmpty();
    }

    [Fact]
    public void RemoveEmptyCreator_does_nothing_when_confirm_cancelled()
    {
        var f = NewFx(); using var _ = f.TempDb;

        SeedMissingVideo(f.Lib);
        f.Vm.Load();

        f.Confirm.NextResult = false;
        f.Vm.EmptyCreators[0].RemoveFromLibraryCommand.Execute(null);

        f.Vm.EmptyCreators.Count.ShouldBe(1);
    }

    // ── TriageChanged event ───────────────────────────────────────────────────

    [Fact]
    public void RemoveOrphanSeries_raises_TriageChanged()
    {
        var f = NewFx(); using var _ = f.TempDb;

        SeedMissingVideo(f.Lib);
        f.Vm.Load();

        int changed = 0;
        f.Vm.TriageChanged += (_, _) => changed++;

        f.Confirm.NextResult = true;
        f.Vm.OrphanSeries[0].RemoveFromLibraryCommand.Execute(null);

        changed.ShouldBe(1);
    }

    // ── HasItems ──────────────────────────────────────────────────────────────

    [Fact]
    public void HasItems_is_false_on_empty_db()
    {
        var f = NewFx(); using var _ = f.TempDb;
        f.Vm.Load();
        f.Vm.HasItems.ShouldBeFalse();
    }

    [Fact]
    public void HasItems_is_true_when_missing_videos_exist()
    {
        var f = NewFx(); using var _ = f.TempDb;
        SeedMissingVideo(f.Lib);
        f.Vm.Load();
        f.Vm.HasItems.ShouldBeTrue();
    }
}
