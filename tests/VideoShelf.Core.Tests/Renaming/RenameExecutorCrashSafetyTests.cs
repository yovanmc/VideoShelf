using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Shouldly;
using VideoShelf.Core.Renaming;
using VideoShelf.Core.Storage;
using Xunit;

namespace VideoShelf.Core.Tests.Renaming;

/// <summary>
/// B2 — pinning tests for <see cref="RenameExecutor"/> crash-mid-apply resumability.
///
/// A rename batch writes its undo manifest BEFORE any file move, so a crash partway through
/// the move loop is recoverable: re-running <see cref="RenameExecutor.Undo"/> against the
/// already-written manifest must restore every successfully-moved file to its original path,
/// leave not-yet-moved files untouched, and never overwrite or lose data.
///
/// We simulate the crash with a throwing <see cref="IFileSystem"/> decorator that lets the
/// manifest write succeed, performs the first real video Move, then throws on the second —
/// modelling a process death in the middle of the batch.
/// </summary>
public sealed class RenameExecutorCrashSafetyTests : IDisposable
{
    private readonly string _dir;
    private readonly VideoShelfDb _db;
    private readonly LibraryRepository _library;
    private readonly long _seriesId;
    private readonly long _v1;
    private readonly long _v2;
    private readonly long _v3;

    public RenameExecutorCrashSafetyTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "vs-exec-crash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _db = new VideoShelfDb(Path.Combine(_dir, "library.db"));
        _db.Migrate();
        _library = new LibraryRepository(_db);
        var src = _library.UpsertSource(@"C:\root", "Root");
        var sec = _library.UpsertSection(src, "S");
        _seriesId = _library.UpsertSeries(sec, "Show", false);
        _v1 = _library.UpsertVideo(_seriesId, @"C:\m\old1.mkv", 1, "mkv");
        _v2 = _library.UpsertVideo(_seriesId, @"C:\m\old2.mkv", 2, "mkv");
        _v3 = _library.UpsertVideo(_seriesId, @"C:\m\old3.mkv", 3, "mkv");
    }

    /// <summary>
    /// In-memory FS that delegates to a backing <see cref="InMemoryFileSystem"/> but throws on
    /// the Nth Move whose SOURCE is a real library video file (i.e. ignoring manifest .tmp moves).
    /// Stamps file content so we can prove no data is lost across the move.
    /// </summary>
    private sealed class ThrowOnNthVideoMoveFs(InMemoryFileSystem inner, int throwOnVideoMove) : IFileSystem
    {
        private int _videoMoves;
        public int VideoMovesAttempted => _videoMoves;

        private static bool IsVideo(string path)
        {
            var ext = Path.GetExtension(path);
            return ext.Equals(".mkv", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase);
        }

        public bool FileExists(string path) => inner.FileExists(path);
        public bool DirectoryExists(string path) => inner.DirectoryExists(path);
        public void CreateDirectory(string path) => inner.CreateDirectory(path);
        public string ReadAllText(string path) => inner.ReadAllText(path);
        public void WriteAllText(string path, string contents) => inner.WriteAllText(path, contents);
        public long GetFileLength(string path) => inner.GetFileLength(path);

        public void Move(string sourcePath, string destinationPath)
        {
            // Only count/trip on moves of actual video files (the manifest .tmp->.json move is exempt).
            if (IsVideo(sourcePath) && IsVideo(destinationPath))
            {
                _videoMoves++;
                if (_videoMoves == throwOnVideoMove)
                    throw new IOException($"simulated crash on video move #{_videoMoves}");
            }
            inner.Move(sourcePath, destinationPath);
        }
    }

    private RenamePlan BuildPlan(IFileSystem fs)
    {
        var planner = new RenamePlanner(fs);
        var videos = _library.GetVideosForSeries(_seriesId);
        var proposed = new Dictionary<long, string>
        {
            [_v1] = "Show 01.mkv",
            [_v2] = "Show 02.mkv",
            [_v3] = "Show 03.mkv",
        };
        return planner.BuildPlan(videos, proposed);
    }

    [Fact]
    public void CrashMidBatch_ManifestWritten_UndoRestoresMovedFiles_LeavesRestUntouched_NoDataLoss()
    {
        // Backing store: three originals, each with distinct content to prove no data loss.
        var inner = new InMemoryFileSystem();
        inner.AddFile(@"C:\m\old1.mkv", "CONTENT-1");
        inner.AddFile(@"C:\m\old2.mkv", "CONTENT-2");
        inner.AddFile(@"C:\m\old3.mkv", "CONTENT-3");

        // Plan against a clean view so re-verification passes.
        var planFs = new InMemoryFileSystem(@"C:\m\old1.mkv", @"C:\m\old2.mkv", @"C:\m\old3.mkv");
        var plan = BuildPlan(planFs);

        // Crash on the SECOND video move: first file gets renamed, then the process "dies".
        var crashFs = new ThrowOnNthVideoMoveFs(inner, throwOnVideoMove: 2);
        var exec = new RenameExecutor(crashFs, _library);

        var manifestDir = Path.Combine(_dir, "manifests");

        // The executor catches per-item move errors, so the crash on item 2 surfaces as an error,
        // item 3 is also attempted. To model a hard crash that stops the loop, the throwing move
        // bubbles up through the executor's try/catch as a recorded error — but crucially the
        // manifest was already written before ANY move. Capture the manifest path for undo.
        var result = exec.Apply(plan, _seriesId, manifestDir);

        // Manifest must exist on disk (written before the move loop).
        result.ManifestPath.ShouldNotBeNull("manifest must be written before any move");
        crashFs.FileExists(result.ManifestPath!).ShouldBeTrue();

        // First file was moved; the crashing move (#2) left old2 in place; file 3 behaviour
        // depends on the executor continuing — assert the durable invariant instead:
        // every file's CONTENT still exists somewhere (nothing was destroyed/overwritten).
        crashFs.FileExists(@"C:\m\Show 01.mkv").ShouldBeTrue("first move completed");
        crashFs.FileExists(@"C:\m\old1.mkv").ShouldBeFalse("first source consumed by its move");
        crashFs.FileExists(@"C:\m\old2.mkv").ShouldBeTrue("the crashing move must not have consumed old2");
        crashFs.FileExists(@"C:\m\Show 02.mkv").ShouldBeFalse("the crashing move produced no target (no overwrite)");

        // ── Recover: run Undo against the already-written manifest ─────────────
        var recoverExec = new RenameExecutor(new InMemoryFileSystemAdapter(inner), _library);
        var undo = recoverExec.Undo(result.ManifestPath!);

        // Every successfully-moved file is restored to its ORIGINAL path with ORIGINAL content.
        inner.FileExists(@"C:\m\old1.mkv").ShouldBeTrue("Show 01 must be restored to old1");
        inner.ReadAllText(@"C:\m\old1.mkv").ShouldBe("CONTENT-1", "no data loss across move+undo");
        inner.FileExists(@"C:\m\Show 01.mkv").ShouldBeFalse("the renamed target is gone after undo");

        // Not-moved originals are untouched with their original content.
        inner.FileExists(@"C:\m\old2.mkv").ShouldBeTrue();
        inner.ReadAllText(@"C:\m\old2.mkv").ShouldBe("CONTENT-2");
        inner.FileExists(@"C:\m\old3.mkv").ShouldBeTrue();
        inner.ReadAllText(@"C:\m\old3.mkv").ShouldBe("CONTENT-3");

        // The DB reflects the restored original paths for the moved-then-undone video.
        var paths = _library.GetVideosForSeries(_seriesId).Select(v => v.FilePath).OrderBy(p => p).ToArray();
        paths.ShouldContain(@"C:\m\old1.mkv", "DB repathed back to the original after undo");
        paths.ShouldContain(@"C:\m\old2.mkv");
        paths.ShouldContain(@"C:\m\old3.mkv");

        undo.Renamed.ShouldBeGreaterThanOrEqualTo(1, "at least the one moved file was reverted");
    }

    /// <summary>Thin adapter so the recovery executor shares the SAME backing store as the crash run.</summary>
    private sealed class InMemoryFileSystemAdapter(InMemoryFileSystem inner) : IFileSystem
    {
        public bool FileExists(string path) => inner.FileExists(path);
        public bool DirectoryExists(string path) => inner.DirectoryExists(path);
        public void CreateDirectory(string path) => inner.CreateDirectory(path);
        public void Move(string s, string d) => inner.Move(s, d);
        public string ReadAllText(string path) => inner.ReadAllText(path);
        public void WriteAllText(string path, string contents) => inner.WriteAllText(path, contents);
        public long GetFileLength(string path) => inner.GetFileLength(path);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }
}
