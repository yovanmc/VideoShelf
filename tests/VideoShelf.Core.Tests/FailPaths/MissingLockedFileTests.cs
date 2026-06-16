using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Shouldly;
using VideoShelf.Core.Models;
using VideoShelf.Core.Renaming;
using VideoShelf.Core.Scanning;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;
using Xunit;

namespace VideoShelf.Core.Tests.FailPaths;

/// <summary>
/// C3 — missing/locked file on scan and rename must fail safe AND be reported, never abort
/// the whole operation or overwrite data.
/// (Recycle keeper-untouched (C3.b) lives with its App-layer owner in
/// VideoShelf.App.Tests/FailPaths/RecycleKeeperSafetyTests.cs.)
/// </summary>
public class MissingLockedFileTests
{
    // ── (a) scan skips an inaccessible entry and continues ────────────────────

    [Fact]
    public void Scan_skips_an_unreadable_subfolder_and_keeps_the_rest()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return; // ACL-deny mechanism below is Windows-specific.

        using var dir = new TempDir();
        dir.Touch("Good Creator/skit.mp4");
        var lockedDir = Path.Combine(dir.Path, "Locked Creator");
        Directory.CreateDirectory(lockedDir);
        File.WriteAllBytes(Path.Combine(lockedDir, "hidden.mp4"), Array.Empty<byte>());

        var user = Environment.UserName;
        // Deny THIS user the ability to list the locked subfolder's contents.
        RunIcacls($"\"{lockedDir}\" /deny \"{user}:(RX)\"");
        // Confirm the deny ACE actually makes the folder unreadable in THIS process; if a backup
        // privilege bypasses it (rare on CI/dev), skip the strong assertion but still run the scan.
        var denyEffective = false;
        try { Directory.EnumerateFiles(lockedDir).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { denyEffective = true; }

        try
        {
            // The whole scan must NOT throw, and the good section must still come through.
            var sections = FolderScanner.Scan(dir.Path).OrderBy(s => s.FolderName).ToList();

            sections.ShouldContain(s => s.FolderName == "Good Creator");
            sections.Single(s => s.FolderName == "Good Creator")
                    .Files.Single().FileName.ShouldBe("skit.mp4");

            // When the deny is effective, the locked section is SKIPPED (proving the catch/continue
            // fired) rather than aborting the whole scan.
            if (denyEffective)
                sections.ShouldNotContain(s => s.FolderName == "Locked Creator");
        }
        finally
        {
            // Restore access so TempDir.Dispose can delete the tree.
            RunIcacls($"\"{lockedDir}\" /remove:d \"{user}\"");
            RunIcacls($"\"{lockedDir}\" /grant \"{user}:(OI)(CI)F\"");
        }
    }

    [Fact]
    public void Scan_of_a_missing_source_root_returns_empty_and_does_not_throw()
    {
        var missing = Path.Combine(Path.GetTempPath(), "vshelf_c3_missing_" + Guid.NewGuid().ToString("N"));
        Directory.Exists(missing).ShouldBeFalse();

        FolderScanner.Scan(missing).ShouldBeEmpty();
    }

    // ── (c) rename where the target became occupied between plan and apply ─────

    [Fact]
    public void Rename_target_occupied_at_apply_time_is_reported_as_conflict_not_overwrite()
    {
        using var dir = new TempDir();
        var db = new VideoShelfDb(Path.Combine(dir.Path, "library.db"));
        db.Migrate();
        var library = new LibraryRepository(db);
        try
        {
            var src = library.UpsertSource(@"C:\root", "Root");
            var sec = library.UpsertSection(src, "S");
            var seriesId = library.UpsertSeries(sec, "Show", false);
            var v1 = library.UpsertVideo(seriesId, @"C:\m\old1.mkv", 1, "mkv");

            // Plan a rename old1 -> "Show 01.mkv".
            var fs = new InMemoryFileSystem(@"C:\m\old1.mkv");
            var planner = new RenamePlanner(fs);
            var videos = library.GetVideosForSeries(seriesId);
            var plan = planner.BuildPlan(videos, new System.Collections.Generic.Dictionary<long, string>
            {
                [v1] = "Show 01.mkv",
            });

            // Between plan and apply, the target path becomes occupied by an unrelated file.
            fs.AddFile(@"C:\m\Show 01.mkv", "pre-existing different content");

            var exec = new RenameExecutor(fs, library);
            var result = exec.Apply(plan, seriesId, Path.Combine(dir.Path, "manifests"));

            // Conflict is REPORTED, nothing renamed, and the pre-existing target is NOT overwritten.
            result.Renamed.ShouldBe(0);
            result.Errors.ShouldNotBeEmpty();
            result.Errors.ShouldContain(e => e.Contains("already exists", StringComparison.OrdinalIgnoreCase));
            fs.ReadAllText(@"C:\m\Show 01.mkv").ShouldBe("pre-existing different content");
            // The original source is untouched and the DB still points at it.
            fs.FileExists(@"C:\m\old1.mkv").ShouldBeTrue();
            library.GetVideosForSeries(seriesId).Single().FilePath.ShouldBe(@"C:\m\old1.mkv");
        }
        finally
        {
            db.Dispose();
        }
    }

    private static void RunIcacls(string args)
    {
        var psi = new ProcessStartInfo("icacls", args)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi);
        p!.WaitForExit(15000);
    }
}
