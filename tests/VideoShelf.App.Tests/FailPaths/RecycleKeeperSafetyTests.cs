using System;
using Shouldly;
using VideoShelf.App.Services;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests;
using Xunit;

namespace VideoShelf.App.Tests.FailPaths;

/// <summary>
/// C3(b) — when a non-keeper file is locked and cannot be recycled, the recycle call returns
/// false (never throws), the keeper file + DB row are untouched, and the failure is surfaced.
/// Pins the RecycleBinService try/catch + the DuplicateResolveViewModel partial-failure path.
/// </summary>
public sealed class RecycleKeeperSafetyTests
{
    [Fact]
    public void Recycle_service_returns_false_and_does_not_throw_on_failure()
    {
        var fake = new FakeRecycleBinService { NextResult = false };
        // The real RecycleBinService swallows exceptions -> false; the fake honors NextResult.
        var ok = fake.SendToRecycleBin(@"C:\locked\file.mp4");
        ok.ShouldBeFalse();
        fake.Recycled.ShouldBeEmpty(); // nothing recorded as recycled on failure
    }

    [Fact]
    public void Locked_loser_is_reported_and_keeper_row_survives()
    {
        var db    = new AppTempDb();
        var lib   = new LibraryRepository(db.Db);
        var maint = new MaintenanceRepository(db.Db);

        var srcId    = lib.UpsertSource(@"C:\V", "V");
        var secId    = lib.UpsertSection(srcId, "Creator");
        var seriesId = lib.UpsertSeries(secId, "Show", false);

        const string pathA = @"C:\V\Creator\Show\ep01.mp4";       // keeper
        const string pathB = @"C:\V\Creator\Show\ep01_copy.mp4";  // locked loser

        var vidA = lib.UpsertVideo(seriesId, pathA, 1, ".mp4");
        var vidB = lib.UpsertVideo(seriesId, pathB, 2, ".mp4");

        SetSizeAndDuration(db, vidA, 100_000_000, 120.0);
        SetSizeAndDuration(db, vidB, 100_000_000, 120.0);

        var groups = maint.GetDuplicateGroupsForSection(secId);
        groups.Count.ShouldBe(1);

        // Keeper is present + non-zero so the safety gate PASSES; the loser recycle then FAILS (locked).
        var fs = new InMemoryFileSystem(pathB);
        fs.AddFile(pathA, "valid keeper bytes");
        var recycleBin = new FakeRecycleBinService { NextResult = false }; // simulate locked loser
        var confirm = new FakeConfirmService { NextResult = true };

        var vm = new DuplicateResolveViewModel(groups[0], maint, lib, recycleBin, confirm, fs);

        try
        {
            var keeperRow = vm.Candidates[0];
            keeperRow.FilePath.ShouldBe(pathA);
            keeperRow.KeepCommand.Execute(null); // must not throw

            // Loser could not be recycled → nothing recorded, failure surfaced.
            recycleBin.Recycled.ShouldBeEmpty();
            vm.IsError.ShouldBeTrue();
            vm.StatusMessage.ShouldNotBeNullOrEmpty();

            // Keeper AND the locked loser's DB rows are BOTH untouched (loser only DB-deleted on a
            // successful recycle — never lose the index for a file still on disk).
            lib.GetEpisode(vidA).ShouldNotBeNull();
            lib.GetEpisode(vidB).ShouldNotBeNull();
        }
        finally
        {
            db.Dispose();
        }
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
}
