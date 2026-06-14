using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Shouldly;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using VideoShelf.App.Services;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.Tests.ViewModels;

/// <summary>
/// Unit tests for <see cref="MaintenanceViewModel"/>.
/// Verifies: summary tiles map to properties, per-source rows are populated,
/// and the rescan command delegates to the scan coordinator.
/// </summary>
public sealed class MaintenanceViewModelTests
{
    // ── Fixture ──────────────────────────────────────────────────────────────

    private sealed record Fx(
        AppTempDb Db,
        LibraryRepository Lib,
        MaintenanceRepository Maintenance,
        SpyScanCoordinator Coordinator,
        MaintenanceViewModel Vm);

    /// <summary>Counts ScanAllAsync calls without touching real disk/media.</summary>
    private sealed class SpyScanCoordinator : IScanCoordinator
    {
        public int ScanCount { get; private set; }
        public bool IsBusy => false;
        public Task<VideoShelf.Core.Scanning.ScanResult> ScanAllAsync(CancellationToken ct)
        {
            ScanCount++;
            return Task.FromResult(new VideoShelf.Core.Scanning.ScanResult(0, 0, 0, 0));
        }
    }

    private static Fx NewFx()
    {
        var db = new AppTempDb();
        var lib = new LibraryRepository(db.Db);
        var maint = new MaintenanceRepository(db.Db);
        var coord = new SpyScanCoordinator();
        var vm = new MaintenanceViewModel(maint, lib, coord);
        return new Fx(db, lib, maint, coord, vm);
    }

    // ── Summary tile properties ───────────────────────────────────────────────

    [Fact]
    public void Load_maps_summary_to_properties_on_empty_db()
    {
        var f = NewFx(); using var _d = f.Db;

        f.Vm.Load();

        f.Vm.MissingCount.ShouldBe(0);
        f.Vm.OrphanSeriesCount.ShouldBe(0);
        f.Vm.EmptyCreatorCount.ShouldBe(0);
        f.Vm.SingleFileSeriesCount.ShouldBe(0);
        f.Vm.DuplicateGroupCount.ShouldBe(0);
        // DB size is > 0 even on empty DB (SQLite header + schema).
        f.Vm.DbSizeText.ShouldNotBeNullOrEmpty();
        f.Vm.DbSizeText.ShouldNotBe("0 B");
    }

    [Fact]
    public void Load_reflects_seeded_missing_video()
    {
        var f = NewFx(); using var _d = f.Db;

        // Seed: source → section → series → video (non-missing).
        var srcId     = f.Lib.UpsertSource(@"C:\V", "V");
        var secId     = f.Lib.UpsertSection(srcId, "Creator1");
        var seriesId  = f.Lib.UpsertSeries(secId, "Series1", false);
        f.Lib.UpsertVideo(seriesId, @"C:\V\Creator1\Series1\e01.mp4", 1, ".mp4");
        // Mark it missing.
        f.Lib.MarkAllMissingForSource(srcId);

        f.Vm.Load();

        f.Vm.MissingCount.ShouldBe(1);
    }

    [Fact]
    public void DbSizeText_is_non_trivial_string()
    {
        var f = NewFx(); using var _d = f.Db;

        f.Vm.Load();

        // Any valid human-readable format: "N.N KB", "N.N MB", etc.
        f.Vm.DbSizeText.ShouldNotBe("–");
        f.Vm.DbSizeText.ShouldContain(" ");
    }

    // ── Per-source rows ───────────────────────────────────────────────────────

    [Fact]
    public void Load_with_no_sources_produces_empty_SourceRows()
    {
        var f = NewFx(); using var _d = f.Db;

        f.Vm.Load();

        f.Vm.SourceRows.ShouldBeEmpty();
    }

    [Fact]
    public void Load_populates_one_SourceRow_per_source()
    {
        var f = NewFx(); using var _d = f.Db;

        f.Lib.UpsertSource(@"C:\Src1", "Source One");
        f.Lib.UpsertSource(@"C:\Src2", "Source Two");

        f.Vm.Load();

        f.Vm.SourceRows.Count.ShouldBe(2);
    }

    [Fact]
    public void SourceRow_DisplayName_and_RootPath_are_populated()
    {
        var f = NewFx(); using var _d = f.Db;

        f.Lib.UpsertSource(@"C:\MyVideos", "My Videos");

        f.Vm.Load();

        var row = f.Vm.SourceRows.Single();
        row.DisplayName.ShouldBe("My Videos");
        row.RootPath.ShouldBe(@"C:\MyVideos");
    }

    [Fact]
    public void SourceRow_LastScanText_is_Never_when_not_scanned()
    {
        var f = NewFx(); using var _d = f.Db;

        f.Lib.UpsertSource(@"C:\V", "V");

        f.Vm.Load();

        f.Vm.SourceRows.Single().LastScanText.ShouldBe("Never");
    }

    [Fact]
    public void SourceRow_LastScanText_is_relative_after_scan_timestamp()
    {
        var f = NewFx(); using var _d = f.Db;

        var srcId = f.Lib.UpsertSource(@"C:\V", "V");
        // Set a scan timestamp 2 hours ago.
        f.Lib.SetSourceLastScanUtc(srcId, System.DateTimeOffset.UtcNow.AddHours(-2));

        f.Vm.Load();

        var text = f.Vm.SourceRows.Single().LastScanText;
        // Should contain "h ago" (2 h ago).
        text.ShouldContain("h ago");
    }

    // ── Rescan command delegates to coordinator ───────────────────────────────

    [Fact]
    public async Task RescanSourceCommand_calls_ScanAllAsync()
    {
        var f = NewFx(); using var _d = f.Db;

        f.Lib.UpsertSource(@"C:\V", "V");
        f.Vm.Load();

        var row = f.Vm.SourceRows.Single();
        await row.RescanSourceCommand.ExecuteAsync(null);

        f.Coordinator.ScanCount.ShouldBe(1);
    }

    // ── SetScanSummary ────────────────────────────────────────────────────────

    [Fact]
    public void SetScanSummary_updates_ScanSummaryText()
    {
        var f = NewFx(); using var _d = f.Db;

        f.Vm.SetScanSummary("Added 5 · updated 1 · restored 0 · missing 2");

        f.Vm.ScanSummaryText.ShouldBe("Added 5 · updated 1 · restored 0 · missing 2");
    }

    // ── RefreshCommand re-loads ───────────────────────────────────────────────

    [Fact]
    public void RefreshCommand_reloads_after_source_change()
    {
        var f = NewFx(); using var _d = f.Db;

        f.Vm.Load();
        f.Vm.SourceRows.ShouldBeEmpty();

        // Add a source AFTER initial load.
        f.Lib.UpsertSource(@"C:\V", "V");
        f.Vm.RefreshCommand.Execute(null);

        f.Vm.SourceRows.Count.ShouldBe(1);
    }

    // ── AppView enum ──────────────────────────────────────────────────────────

    [Fact]
    public void AppView_enum_contains_Maintenance()
    {
        // Regression guard: the enum must include Maintenance (M18-E).
        var values = System.Enum.GetNames<AppView>();
        values.ShouldContain("Maintenance");
    }

    // ── HarnessOptions parse ──────────────────────────────────────────────────

    [Fact]
    public void HarnessOptions_parse_accepts_Maintenance_view()
    {
        var opts = VideoShelf.App.Harness.HarnessOptions.Parse(
            new[] { "--view", "Maintenance", "--done-signal", @"C:\s.txt" });

        opts.View.ShouldBe("Maintenance");
        opts.IsHarness.ShouldBeTrue();
    }
}
