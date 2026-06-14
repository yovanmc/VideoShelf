using System;
using System.IO;
using Shouldly;
using VideoShelf.App.Services;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Models;
using VideoShelf.Core.Renaming;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests;
using Xunit;

namespace VideoShelf.App.Tests;

/// <summary>
/// M18-G unit tests for <see cref="DuplicateResolveViewModel"/>.
///
/// Safety-gate coverage:
///   (a) Keeper present + nonzero → losers recycled + DB rows deleted + Resolved raised.
///   (b) Keeper missing (GetFileLength returns -1) → nothing recycled, error surfaced.
///   (c) Keeper zero-length (GetFileLength returns 0) → nothing recycled, error surfaced.
///   (d) NotADuplicate → all cross-pairs dismissed + Resolved raised + pair never re-flags.
///   (e) Confirm declined → no recycle, no DB delete.
/// </summary>
public sealed class DuplicateResolveViewModelTests
{
    // ── Fixture ───────────────────────────────────────────────────────────────

    private sealed record Fx(
        AppTempDb TempDb,
        LibraryRepository Lib,
        MaintenanceRepository Maintenance,
        FakeRecycleBinService RecycleBin,
        FakeConfirmService Confirm,
        InMemoryFileSystem Fs,
        DuplicateResolveViewModel Vm,
        long VideoIdA,
        long VideoIdB,
        string PathA,
        string PathB);

    /// <summary>
    /// Seeds two duplicate videos (same size + duration) under the same creator/section
    /// and builds the DuplicateResolveViewModel.
    /// <paramref name="keeperFileExists"/> controls whether path A exists in the fake FS.
    /// <paramref name="keeperFileLength"/> controls InMemoryFileSystem content for path A.
    /// </summary>
    private static Fx NewFx(bool keeperFileExists = true, string keeperContent = "data")
    {
        var db    = new AppTempDb();
        var lib   = new LibraryRepository(db.Db);
        var maint = new MaintenanceRepository(db.Db);

        var srcId    = lib.UpsertSource(@"C:\V", "V");
        var secId    = lib.UpsertSection(srcId, "Creator");
        var seriesId = lib.UpsertSeries(secId, "Show", false);

        const string pathA = @"C:\V\Creator\Show\ep01.mp4";
        const string pathB = @"C:\V\Creator\Show\ep01_copy.mp4";

        var vidA = lib.UpsertVideo(seriesId, pathA, 1, ".mp4");
        var vidB = lib.UpsertVideo(seriesId, pathB, 2, ".mp4");

        // Mark both with matching size + duration so they appear as duplicates.
        SetSizeAndDuration(db, vidA, sizeBytes: 100_000_000, duration: 120.0);
        SetSizeAndDuration(db, vidB, sizeBytes: 100_000_000, duration: 120.0);

        // Build a DuplicateGroup from the repo.
        var groups = maint.GetDuplicateGroupsForSection(secId);
        groups.Count.ShouldBe(1, "test setup: expected exactly one duplicate group");

        var recycleBin = new FakeRecycleBinService();
        var confirm    = new FakeConfirmService { NextResult = true };

        // InMemoryFileSystem: path A exists with content; path B always exists.
        var fs = keeperFileExists
            ? new InMemoryFileSystem(pathB)  // B always exists; A conditionally
            : new InMemoryFileSystem(pathB);

        if (keeperFileExists)
            fs.AddFile(pathA, keeperContent);

        var vm = new DuplicateResolveViewModel(groups[0], maint, lib, recycleBin, confirm, fs);
        return new Fx(db, lib, maint, recycleBin, confirm, fs, vm, vidA, vidB, pathA, pathB);
    }

    private static void SetSizeAndDuration(AppTempDb db, long videoId, long sizeBytes, double duration)
    {
        using var conn = db.Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE videos SET size_bytes = @s, duration = @d WHERE id = @id";
        cmd.Parameters.AddWithValue("@s", sizeBytes);
        cmd.Parameters.AddWithValue("@d", duration);
        cmd.Parameters.AddWithValue("@id", videoId);
        cmd.ExecuteNonQuery();
    }

    private static bool VideoExistsInDb(LibraryRepository lib, long videoId)
        => lib.GetEpisode(videoId) is not null;

    // ── (a) Keeper present + non-zero → loser recycled + DB row deleted + Resolved raised ──

    [Fact]
    public void Keep_KeeperPresent_RecyclesLoserAndDeletesFromDb()
    {
        var f = NewFx(keeperFileExists: true, keeperContent: "some video data"); using var _ = f.TempDb;

        bool resolvedFired = false;
        f.Vm.Resolved += (_, _) => resolvedFired = true;

        // "Keep" path A (the first candidate)
        var keeperRow = f.Vm.Candidates[0]; // PathA
        keeperRow.FilePath.ShouldBe(f.PathA);
        keeperRow.KeepCommand.Execute(null);

        // Loser (B) sent to recycle bin.
        f.RecycleBin.Recycled.ShouldContain(f.PathB);

        // Loser's DB row deleted; keeper's row still present.
        VideoExistsInDb(f.Lib, f.VideoIdB).ShouldBeFalse("loser's DB row must be deleted after recycle");
        VideoExistsInDb(f.Lib, f.VideoIdA).ShouldBeTrue("keeper's DB row must survive");

        // Resolved event raised.
        resolvedFired.ShouldBeTrue();

        // No error message.
        f.Vm.IsError.ShouldBeFalse();
    }

    // ── (b) Keeper missing → nothing recycled, error surfaced ────────────────

    [Fact]
    public void Keep_KeeperMissing_RecyclesNothing_SurfacesError()
    {
        // Path A does NOT exist in the fake FS (keeperFileExists = false).
        var f = NewFx(keeperFileExists: false); using var _ = f.TempDb;

        bool resolvedFired = false;
        f.Vm.Resolved += (_, _) => resolvedFired = true;

        var keeperRow = f.Vm.Candidates[0]; // PathA — keeper
        keeperRow.KeepCommand.Execute(null);

        // Nothing recycled.
        f.RecycleBin.Recycled.ShouldBeEmpty("keeper missing → nothing should be recycled");

        // Both DB rows untouched.
        VideoExistsInDb(f.Lib, f.VideoIdA).ShouldBeTrue();
        VideoExistsInDb(f.Lib, f.VideoIdB).ShouldBeTrue();

        // Error surfaced.
        f.Vm.IsError.ShouldBeTrue();
        f.Vm.StatusMessage.ShouldNotBeNullOrEmpty();

        // Resolved NOT raised.
        resolvedFired.ShouldBeFalse();
    }

    // ── (c) Keeper zero-length → nothing recycled, error surfaced ─────────────

    [Fact]
    public void Keep_KeeperZeroLength_RecyclesNothing_SurfacesError()
    {
        // Path A exists but is empty (zero bytes → GetFileLength returns 0).
        var f = NewFx(keeperFileExists: true, keeperContent: ""); using var _ = f.TempDb;

        // InMemoryFileSystem returns 0 for an empty string → UTF8.GetByteCount("") = 0.
        f.Fs.GetFileLength(f.PathA).ShouldBe(0L, "empty content should be zero bytes");

        bool resolvedFired = false;
        f.Vm.Resolved += (_, _) => resolvedFired = true;

        var keeperRow = f.Vm.Candidates[0];
        keeperRow.KeepCommand.Execute(null);

        f.RecycleBin.Recycled.ShouldBeEmpty("zero-length keeper → nothing should be recycled");
        VideoExistsInDb(f.Lib, f.VideoIdA).ShouldBeTrue();
        VideoExistsInDb(f.Lib, f.VideoIdB).ShouldBeTrue();
        f.Vm.IsError.ShouldBeTrue();
        resolvedFired.ShouldBeFalse();
    }

    // ── (d) Dismiss persists and pair never re-flags ──────────────────────────

    [Fact]
    public void NotADuplicate_DismissesAllPairs_PairNoLongerReflags()
    {
        var f = NewFx(); using var _ = f.TempDb;

        bool resolvedFired = false;
        f.Vm.Resolved += (_, _) => resolvedFired = true;

        f.Vm.NotADuplicateCommand.Execute(null);

        // Resolved raised.
        resolvedFired.ShouldBeTrue();

        // Nothing recycled.
        f.RecycleBin.Recycled.ShouldBeEmpty();

        // Pair is now dismissed in the DB.
        f.Maintenance.IsDuplicatePairDismissed(f.VideoIdA, f.VideoIdB).ShouldBeTrue();
        f.Maintenance.IsDuplicatePairDismissed(f.VideoIdB, f.VideoIdA).ShouldBeTrue(); // order-independent

        // GetDuplicateGroups now returns an empty list for this section.
        var sectionId = GetSectionId(f.Lib, f.VideoIdA);
        var groups = f.Maintenance.GetDuplicateGroupsForSection(sectionId);
        groups.ShouldBeEmpty("dismissed pair must not re-flag as duplicate");
    }

    private static long GetSectionId(LibraryRepository lib, long videoId)
    {
        var ep = lib.GetEpisode(videoId);
        ep.ShouldNotBeNull();
        // Walk up: video → series → section. We need GetEpisode to give seriesId.
        // Use GetSourceRootForVideo as a proxy to confirm the video exists, then
        // look up section via a different path.
        // Since we need the section, query directly.
        return GetSectionIdDirect(lib, videoId);
    }

    private static long GetSectionIdDirect(LibraryRepository lib, long videoId)
    {
        // LibraryRepository doesn't expose a direct GetSectionIdForVideo, but we can
        // derive it by querying through GetEpisode → series → section.
        // For tests we use the fact that we know the section was the only one created.
        // To avoid coupling to internal SQL, use the sections list:
        var allSections = lib.GetSectionSummaries();
        allSections.Count.ShouldBe(1, "test fixture has exactly one section");
        return allSections[0].SectionId;
    }

    // ── (e) Confirm declined → no recycle, no DB delete ──────────────────────

    [Fact]
    public void Keep_ConfirmDeclined_RecyclesNothing()
    {
        var f = NewFx(keeperFileExists: true, keeperContent: "video data"); using var _ = f.TempDb;

        // Simulate the user clicking "No" in the confirm dialog.
        f.Confirm.NextResult = false;

        bool resolvedFired = false;
        f.Vm.Resolved += (_, _) => resolvedFired = true;

        var keeperRow = f.Vm.Candidates[0];
        keeperRow.KeepCommand.Execute(null);

        f.RecycleBin.Recycled.ShouldBeEmpty("confirm declined → nothing recycled");
        VideoExistsInDb(f.Lib, f.VideoIdA).ShouldBeTrue();
        VideoExistsInDb(f.Lib, f.VideoIdB).ShouldBeTrue();
        resolvedFired.ShouldBeFalse();
        f.Vm.IsError.ShouldBeFalse("no error on a user cancel");
    }

    // ── Candidate row display properties ─────────────────────────────────────

    [Fact]
    public void Candidates_ExposeCorrectDisplayProperties()
    {
        var f = NewFx(keeperFileExists: true, keeperContent: "x"); using var _ = f.TempDb;

        f.Vm.Candidates.Count.ShouldBe(2);

        // Both candidates have a filename (not the full path).
        foreach (var c in f.Vm.Candidates)
        {
            c.FileName.ShouldNotBeNullOrEmpty();
            c.FileName.ShouldNotContain('\\'); // FileName should be just the filename, not a path
            c.SizeText.ShouldNotBe("–", "size was seeded — should display a size");
            c.DurationText.ShouldNotBe("–", "duration was seeded — should display duration");
            // Resolution: we didn't set width/height so it should be "–".
            c.ResolutionText.ShouldBe("–");
        }
    }

    // ── Resolution text when width+height are set ────────────────────────────

    [Fact]
    public void CandidateResolutionText_DisplaysCorrectly_WhenSet()
    {
        var model = new DuplicateVideo(1, 1, "Creator", "Show", @"C:\v.mp4",
            100_000_000L, 120.0, 1920, 1080);
        var group = new DuplicateGroup(100_000_000L, 120,
            new[] { model,
                    new DuplicateVideo(2, 1, "Creator", "Show", @"C:\v2.mp4",
                        100_000_000L, 120.0, 1920, 1080) });

        var db   = new AppTempDb();
        var lib  = new LibraryRepository(db.Db);
        var maint = new MaintenanceRepository(db.Db);
        var vm = new DuplicateResolveViewModel(group, maint, lib,
            new FakeRecycleBinService(), new FakeConfirmService(),
            new InMemoryFileSystem());
        db.Dispose();

        vm.Candidates[0].ResolutionText.ShouldBe("1920×1080");
    }
}
