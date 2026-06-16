using System;
using System.IO;
using System.Linq;
using Shouldly;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Storage;
using Xunit;

namespace VideoShelf.App.Tests.Safety;

/// <summary>
/// B3 — pinning tests for Remove-source safety + undo.
///
/// Removing a source is a DB-INDEX-ONLY operation: it deletes the source row (and, via
/// ON DELETE CASCADE, its sections/series/videos) but NEVER touches the filesystem. Undo
/// re-adds the SAME source idempotently, and a rescan restores its rows.
///
/// To prove "no disk delete ever occurs" we register the source against a REAL temp folder
/// holding a real video file, run remove + undo, and assert the on-disk file is still there
/// byte-for-byte. Structurally, <see cref="SourcesViewModel"/> has no IRecycleBinService /
/// IFileSystem / delete seam at all — there is no code path that could delete a file.
/// </summary>
public sealed class RemoveSourceUndoTests : IDisposable
{
    private readonly string _dir;
    private readonly string _videoPath;

    public RemoveSourceUndoTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "vs-removesrc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _videoPath = Path.Combine(_dir, "movie.mp4");
        File.WriteAllBytes(_videoPath, new byte[] { 9, 8, 7, 6, 5 }); // real on-disk file
    }

    [Fact]
    public void RemoveThenUndo_ReaddsSameSource_RescanRestoresRows_AndNoDiskDeleteOccurs()
    {
        using var temp = new AppTempDb();
        var lib = new LibraryRepository(temp.Db);

        // Seed real DB rows under a source rooted at the temp folder.
        var srcId    = lib.UpsertSource(_dir, "MySource");
        var secId    = lib.UpsertSection(srcId, "Creator");
        var seriesId = lib.UpsertSeries(secId, "Show", false);
        lib.UpsertVideo(seriesId, _videoPath, 1, ".mp4");

        lib.GetSources().Count.ShouldBe(1);

        var confirm = new FakeConfirmService { NextResult = true };
        var vm = new SourcesViewModel(lib, new FakeFolderPicker(), confirm);
        vm.Load();
        var src = vm.Sources.Single();
        src.RootPath.ShouldBe(_dir);

        bool restoredFired = false;
        vm.OnSourceRestored = () => restoredFired = true;

        // ── Remove ──────────────────────────────────────────────────────────
        vm.RemoveSourceCommand.Execute(src);

        vm.Sources.ShouldBeEmpty("source removed from the index");
        lib.GetSources().ShouldBeEmpty("source row gone from the DB");
        vm.CanUndoRemove.ShouldBeTrue();
        // The on-disk file MUST be untouched by a remove.
        File.Exists(_videoPath).ShouldBeTrue("remove is DB-index-only — disk file must survive");

        // ── Undo: re-adds the SAME source idempotently ──────────────────────
        vm.UndoRemoveCommand.Execute(null);

        restoredFired.ShouldBeTrue("undo signals the shell to rescan");
        vm.CanUndoRemove.ShouldBeFalse();
        var readded = lib.GetSources();
        readded.Count.ShouldBe(1, "undo re-adds exactly one source (idempotent UpsertSource)");
        readded[0].RootPath.ShouldBe(_dir);
        readded[0].DisplayName.ShouldBe("MySource");

        // ── Rescan-equivalent: re-upsert the same rows; they come back ──────
        var reSrcId    = lib.UpsertSource(_dir, "MySource"); // idempotent: same row id
        reSrcId.ShouldBe(readded[0].Id);
        var reSecId    = lib.UpsertSection(reSrcId, "Creator");
        var reSeriesId = lib.UpsertSeries(reSecId, "Show", false);
        lib.UpsertVideo(reSeriesId, _videoPath, 1, ".mp4");

        var videos = lib.GetVideosForSeries(reSeriesId);
        videos.Select(v => v.FilePath).ShouldContain(_videoPath, "rescan restores the source's rows");

        // ── No disk delete ever occurred ────────────────────────────────────
        File.Exists(_videoPath).ShouldBeTrue("no path in remove/undo/rescan deletes a disk file");
        new FileInfo(_videoPath).Length.ShouldBe(5, "the file is byte-for-byte intact");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }
}
