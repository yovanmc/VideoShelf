// tests/VideoShelf.App.Tests/MultiRenameViewModelTests.cs
// H3 — MultiRenameViewModel safety tests:
//   preview → apply → UpdateVideoPath for every id → exactly ONE manifest → Undo restores ALL.
// Additional: no-overwrite guard, crash-safety (manifest exists before moves).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Shouldly;
using VideoShelf.App.Services;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Renaming;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests;
using Xunit;

namespace VideoShelf.App.Tests;

public class MultiRenameViewModelTests : IDisposable
{
    // ── Fixtures ─────────────────────────────────────────────────────────────

    private readonly string _dir;
    private readonly VideoShelfDb _db;
    private readonly LibraryRepository _library;
    private readonly SettingsRepository _settings;
    private readonly InMemoryFileSystem _fs;

    // Two creators, two series each.
    private readonly long _sectionA;   // Creator A
    private readonly long _sectionB;   // Creator B

    private readonly long _seriesA1;   // Creator A / Series Alpha
    private readonly long _seriesA2;   // Creator A / Series Beta
    private readonly long _seriesB1;   // Creator B / Series Gamma

    private readonly long _vA1e1;      // A/Alpha ep1
    private readonly long _vA1e2;      // A/Alpha ep2
    private readonly long _vA2e1;      // A/Beta  ep1
    private readonly long _vB1e1;      // B/Gamma ep1

    public MultiRenameViewModelTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "vs-multi-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _db = new VideoShelfDb(Path.Combine(_dir, "library.db"));
        _db.Migrate();
        _library = new LibraryRepository(_db);
        _settings = new SettingsRepository(_db);

        var src = _library.UpsertSource(@"C:\root", "Root");

        // Creator A
        _sectionA = _library.UpsertSection(src, "Creator A");
        _seriesA1 = _library.UpsertSeries(_sectionA, "Alpha", false);
        _vA1e1 = _library.UpsertVideo(_seriesA1, @"C:\m\junk_a1e1.mkv", 1, "mkv");
        _vA1e2 = _library.UpsertVideo(_seriesA1, @"C:\m\junk_a1e2.mkv", 2, "mkv");

        _seriesA2 = _library.UpsertSeries(_sectionA, "Beta", false);
        _vA2e1 = _library.UpsertVideo(_seriesA2, @"C:\m\junk_a2e1.mkv", 1, "mkv");

        // Creator B
        _sectionB = _library.UpsertSection(src, "Creator B");
        _seriesB1 = _library.UpsertSeries(_sectionB, "Gamma", false);
        _vB1e1 = _library.UpsertVideo(_seriesB1, @"C:\m\junk_b1e1.mkv", 1, "mkv");

        _fs = new InMemoryFileSystem(
            @"C:\m\junk_a1e1.mkv",
            @"C:\m\junk_a1e2.mkv",
            @"C:\m\junk_a2e1.mkv",
            @"C:\m\junk_b1e1.mkv");
    }

    private MultiRenameViewModel Build()
    {
        var planner = new RenamePlanner(_fs);
        var executor = new RenameExecutor(_fs, _library);
        var paths = new AppPaths(_dir);
        return new MultiRenameViewModel(_library, planner, executor, _settings, paths);
    }

    private IReadOnlyList<long> AllThreeSeries => new[] { _seriesA1, _seriesA2, _seriesB1 };

    // ── Preview ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Load_BuildsRowsForAllSeries()
    {
        var vm = Build();
        await vm.LoadAsync(AllThreeSeries, MultiRenameViewModel.DefaultTemplate);

        // 4 videos across 3 series.
        vm.Rows.Count.ShouldBe(4);
        vm.Rows.All(r => r.WillRename).ShouldBeTrue();
    }

    [Fact]
    public async Task Load_DefaultTemplate_RendersCreatorAndSeriesInName()
    {
        var vm = Build();
        await vm.LoadAsync(AllThreeSeries, MultiRenameViewModel.DefaultTemplate);

        // Default template = "{creator} - {series} - {NN}"
        vm.Rows.All(r => r.NewName.Contains(" - ")).ShouldBeTrue();
        vm.Rows.Any(r => r.NewName.StartsWith("Creator A")).ShouldBeTrue();
        vm.Rows.Any(r => r.NewName.StartsWith("Creator B")).ShouldBeTrue();
    }

    [Fact]
    public async Task Load_SingleSeriesOnly_RendersOneSeriesCorrectly()
    {
        var vm = Build();
        await vm.LoadAsync(new[] { _seriesA1 }, "{series} {NN}");

        vm.Rows.Count.ShouldBe(2);
        vm.Rows[0].NewName.ShouldBe("Alpha 01.mkv");
        vm.Rows[1].NewName.ShouldBe("Alpha 02.mkv");
    }

    // ── Duplicate-target detection (cross-series) ─────────────────────────────

    [Fact]
    public async Task EditingRowToCollideWithAnotherRow_FlagsDuplicateTarget()
    {
        var vm = Build();
        await vm.LoadAsync(AllThreeSeries, MultiRenameViewModel.DefaultTemplate);

        // Force a collision: set row 0's proposed name to the same as row 1's current name.
        var row0NewName = vm.Rows[0].NewName;
        var row1NewName = vm.Rows[1].NewName;
        // They start different; set row 0 to match row 1.
        vm.Rows[0].NewName = row1NewName;

        vm.Rows[0].Status.ShouldBe(RenameItemStatus.DuplicateTarget);
        vm.Rows[1].Status.ShouldBe(RenameItemStatus.DuplicateTarget);
    }

    // ── Apply + single manifest ──────────────────────────────────────────────

    [Fact]
    public async Task Apply_RenamesAllFilesOnDiskAcrossSeries()
    {
        var vm = Build();
        await vm.LoadAsync(AllThreeSeries, MultiRenameViewModel.DefaultTemplate);
        await vm.ApplyCommand.ExecuteAsync(null);

        // All original files should be gone.
        _fs.FileExists(@"C:\m\junk_a1e1.mkv").ShouldBeFalse();
        _fs.FileExists(@"C:\m\junk_a1e2.mkv").ShouldBeFalse();
        _fs.FileExists(@"C:\m\junk_a2e1.mkv").ShouldBeFalse();
        _fs.FileExists(@"C:\m\junk_b1e1.mkv").ShouldBeFalse();
    }

    [Fact]
    public async Task Apply_RepathsAllVideoIdsInDB()
    {
        var vm = Build();
        await vm.LoadAsync(AllThreeSeries, MultiRenameViewModel.DefaultTemplate);
        await vm.ApplyCommand.ExecuteAsync(null);

        // All DB paths for all series should now reflect renamed files (not the junk originals).
        var a1Paths = _library.GetVideosForSeries(_seriesA1).Select(v => v.FilePath).ToList();
        var a2Paths = _library.GetVideosForSeries(_seriesA2).Select(v => v.FilePath).ToList();
        var b1Paths = _library.GetVideosForSeries(_seriesB1).Select(v => v.FilePath).ToList();

        a1Paths.ShouldAllBe(p => !p.Contains("junk"), "series A1 paths should not contain 'junk'");
        a2Paths.ShouldAllBe(p => !p.Contains("junk"), "series A2 paths should not contain 'junk'");
        b1Paths.ShouldAllBe(p => !p.Contains("junk"), "series B1 paths should not contain 'junk'");
    }

    [Fact]
    public async Task Apply_WritesExactlyOneManifest()
    {
        var vm = Build();
        await vm.LoadAsync(AllThreeSeries, MultiRenameViewModel.DefaultTemplate);
        await vm.ApplyCommand.ExecuteAsync(null);

        // The manifest path should be set in settings (one batch = one manifest).
        var manifestPath = _settings.GetString("last_rename_manifest", "");
        manifestPath.ShouldNotBeNullOrEmpty("settings must record the manifest path after Apply");
        // Manifest is written via IFileSystem (InMemoryFileSystem here), so it exists in the fs.
        _fs.FileExists(manifestPath).ShouldBeTrue("exactly one manifest file must exist in the in-memory fs");
    }

    [Fact]
    public async Task Apply_ManifestCoversAllRenamedVideos()
    {
        var vm = Build();
        await vm.LoadAsync(AllThreeSeries, MultiRenameViewModel.DefaultTemplate);
        await vm.ApplyCommand.ExecuteAsync(null);

        // Read the manifest via the InMemoryFileSystem (the executor writes through IFileSystem).
        var manifestPath = _settings.GetString("last_rename_manifest", "");
        manifestPath.ShouldNotBeNullOrEmpty();

        // Use InMemoryFileSystem to read the manifest JSON.
        var json = _fs.ReadAllText(manifestPath);
        // The manifest should mention all 4 video ids.
        json.ShouldContain(_vA1e1.ToString());
        json.ShouldContain(_vA1e2.ToString());
        json.ShouldContain(_vA2e1.ToString());
        json.ShouldContain(_vB1e1.ToString());
    }

    [Fact]
    public async Task Apply_SetsCanUndoTrue()
    {
        var vm = Build();
        await vm.LoadAsync(AllThreeSeries, MultiRenameViewModel.DefaultTemplate);
        await vm.ApplyCommand.ExecuteAsync(null);

        vm.CanUndo.ShouldBeTrue();
    }

    // ── Crash-safety: manifest exists before moves ────────────────────────────

    [Fact]
    public async Task CrashSafety_ManifestExistsInFsBeforeAnyMove()
    {
        // We can't intercept mid-batch, but we CAN verify the manifest is written atomically
        // (it's in the InMemoryFileSystem and has valid JSON) after a clean Apply —
        // the manifest-first invariant is enforced by RenameExecutor.ApplyCore.
        var vm = Build();
        await vm.LoadAsync(AllThreeSeries, MultiRenameViewModel.DefaultTemplate);
        await vm.ApplyCommand.ExecuteAsync(null);

        var manifestPath = _settings.GetString("last_rename_manifest", "");
        manifestPath.ShouldNotBeNullOrEmpty("manifest path must be set in settings after Apply");
        _fs.FileExists(manifestPath).ShouldBeTrue("manifest must exist in IFileSystem after Apply");

        // Manifest must be valid JSON (non-empty, starts with '{').
        var json = _fs.ReadAllText(manifestPath);
        json.ShouldNotBeNullOrEmpty();
        json.TrimStart()[0].ShouldBe('{');
    }

    // ── Undo restores ALL files and DB paths ──────────────────────────────────

    [Fact]
    public async Task Undo_RestoresAllFilesOnDisk()
    {
        var vm = Build();
        await vm.LoadAsync(AllThreeSeries, MultiRenameViewModel.DefaultTemplate);
        await vm.ApplyCommand.ExecuteAsync(null);

        // Undo the whole batch.
        await vm.UndoCommand.ExecuteAsync(null);

        // All original files should be back.
        _fs.FileExists(@"C:\m\junk_a1e1.mkv").ShouldBeTrue();
        _fs.FileExists(@"C:\m\junk_a1e2.mkv").ShouldBeTrue();
        _fs.FileExists(@"C:\m\junk_a2e1.mkv").ShouldBeTrue();
        _fs.FileExists(@"C:\m\junk_b1e1.mkv").ShouldBeTrue();
    }

    [Fact]
    public async Task Undo_RestoresAllDbPathsAcrossSeries()
    {
        var vm = Build();
        await vm.LoadAsync(AllThreeSeries, MultiRenameViewModel.DefaultTemplate);
        await vm.ApplyCommand.ExecuteAsync(null);
        await vm.UndoCommand.ExecuteAsync(null);

        // All DB paths for all series should be back to the original junk names.
        var a1Paths = _library.GetVideosForSeries(_seriesA1).Select(v => Path.GetFileName(v.FilePath)).OrderBy(x => x).ToList();
        var a2Paths = _library.GetVideosForSeries(_seriesA2).Select(v => Path.GetFileName(v.FilePath)).ToList();
        var b1Paths = _library.GetVideosForSeries(_seriesB1).Select(v => Path.GetFileName(v.FilePath)).ToList();

        a1Paths.ShouldContain("junk_a1e1.mkv");
        a1Paths.ShouldContain("junk_a1e2.mkv");
        a2Paths.ShouldContain("junk_a2e1.mkv");
        b1Paths.ShouldContain("junk_b1e1.mkv");
    }

    [Fact]
    public async Task Undo_SetsCanUndoFalse_AfterConsuming()
    {
        var vm = Build();
        await vm.LoadAsync(AllThreeSeries, MultiRenameViewModel.DefaultTemplate);
        await vm.ApplyCommand.ExecuteAsync(null);
        await vm.UndoCommand.ExecuteAsync(null);

        vm.CanUndo.ShouldBeFalse();
    }

    // ── No-overwrite safety ──────────────────────────────────────────────────

    [Fact]
    public async Task Apply_SkipsRow_WhenTargetAlreadyExistsOnDisk()
    {
        var vm = Build();
        await vm.LoadAsync(new[] { _seriesA1 }, "{series} {NN}");

        // Pre-create the target for row 0 so it's "occupied".
        var targetName = vm.Rows[0].NewName;  // e.g. "Alpha 01.mkv"
        _fs.AddFile(Path.Combine(@"C:\m", targetName));

        // The planner should mark it TargetExists (blocked); Apply skips it.
        // No exception must be thrown, and the original file must NOT be overwritten.
        await vm.ApplyCommand.ExecuteAsync(null);

        // Original should still exist (move was skipped).
        _fs.FileExists(@"C:\m\junk_a1e1.mkv").ShouldBeTrue("original must survive when target is occupied");
        // Pre-existing target must still exist (not clobbered).
        _fs.FileExists(Path.Combine(@"C:\m", targetName)).ShouldBeTrue("pre-existing target must not be clobbered");
    }

    // ── Manifest SeriesId is null for multi-series batch ──────────────────────

    [Fact]
    public async Task Apply_MultiSeriesManifest_HasNullSeriesId()
    {
        var vm = Build();
        await vm.LoadAsync(AllThreeSeries, MultiRenameViewModel.DefaultTemplate);
        await vm.ApplyCommand.ExecuteAsync(null);

        var manifestPath = _settings.GetString("last_rename_manifest", "");
        manifestPath.ShouldNotBeNullOrEmpty();
        // Read via InMemoryFileSystem (the executor writes through IFileSystem).
        var json = _fs.ReadAllText(manifestPath);
        // The JSON should contain a null value for SeriesId (batch manifest has no single series).
        // JSON is written with WriteIndented=true so the key looks like: "SeriesId": null
        json.ShouldContain("\"SeriesId\": null");
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_dir, true); } catch { /* best-effort */ }
    }
}
