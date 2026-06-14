using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Shouldly;
using VideoShelf.App.Services;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Scanning;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;

namespace VideoShelf.App.Tests;

/// <summary>
/// Group I: scan-diff feedback surfacing.
/// Verifies: coordinator aggregates multi-source results; FormatScanSummary produces the right text;
/// SettingsViewModel persists/restores the summary string.
/// </summary>
public sealed class ScanSummaryTests
{
    // ── FormatScanSummary ────────────────────────────────────────────────────

    [Fact]
    public void FormatScanSummary_formats_all_fields()
    {
        var result = new ScanResult(Added: 12, Updated: 3, Restored: 1, Missing: 1);

        var text = MainViewModel.FormatScanSummary(result);

        text.ShouldBe("Added 12 · updated 3 · restored 1 · missing 1");
    }

    [Fact]
    public void FormatScanSummary_zero_values_are_shown()
    {
        var result = new ScanResult(0, 0, 0, 0);

        var text = MainViewModel.FormatScanSummary(result);

        text.ShouldBe("Added 0 · updated 0 · restored 0 · missing 0");
    }

    // ── Coordinator aggregation ──────────────────────────────────────────────

    [Fact]
    public async Task ScanAll_aggregates_results_across_two_sources()
    {
        using var temp = new AppTempDb();
        using var dirA = new TempDir();
        using var dirB = new TempDir();

        // Source A: Creator A / a.mp4
        dirA.Touch("Creator A/a.mp4");
        // Source B: Creator B / b.mp4 + c.mp4
        dirB.Touch("Creator B/b.mp4");
        dirB.Touch("Creator B/c.mp4");

        var lib = new LibraryRepository(temp.Db);
        lib.UpsertSource(dirA.Path, "A");
        lib.UpsertSource(dirB.Path, "B");

        var scan = new ScanService(temp.Db, lib);
        var coordinator = new ScanCoordinator(lib, scan);

        var result = await coordinator.ScanAllAsync(CancellationToken.None);

        // First scan: 3 files total added (1 from A, 2 from B), 0 updated/restored/missing.
        result.Added.ShouldBe(3);
        result.Updated.ShouldBe(0);
        result.Restored.ShouldBe(0);
        result.Missing.ShouldBe(0);
    }

    [Fact]
    public async Task ScanAll_aggregates_missing_across_sources()
    {
        using var temp = new AppTempDb();
        using var dirA = new TempDir();
        using var dirB = new TempDir();

        // Seed both sources with one file each.
        dirA.Touch("Creator A/a.mp4");
        dirB.Touch("Creator B/b.mp4");

        var lib = new LibraryRepository(temp.Db);
        lib.UpsertSource(dirA.Path, "A");
        lib.UpsertSource(dirB.Path, "B");

        var scan = new ScanService(temp.Db, lib);
        var coordinator = new ScanCoordinator(lib, scan);

        // First scan: both added.
        await coordinator.ScanAllAsync(CancellationToken.None);

        // Delete file in source A so it goes missing on the second scan.
        File.Delete(Path.Combine(dirA.Path, "Creator A", "a.mp4"));

        var result = await coordinator.ScanAllAsync(CancellationToken.None);

        // Source A: 0 added, 0 updated, 0 restored, 1 missing
        // Source B: 0 added, 1 updated, 0 restored, 0 missing
        result.Missing.ShouldBe(1);
        result.Updated.ShouldBe(1);
    }

    // ── SettingsViewModel persist/restore ────────────────────────────────────

    [Fact]
    public void LastScanSummaryText_starts_empty_on_fresh_db()
    {
        using var temp = new AppTempDb();
        var vm = new SettingsViewModel(new SettingsRepository(temp.Db));

        vm.LastScanSummaryText.ShouldBeEmpty();
    }

    [Fact]
    public void MarkScanned_updates_LastScanSummaryText()
    {
        using var temp = new AppTempDb();
        var vm = new SettingsViewModel(new SettingsRepository(temp.Db));
        const string summary = "Added 5 · updated 2 · restored 1 · missing 0";

        vm.MarkScanned(summary);

        vm.LastScanSummaryText.ShouldBe(summary);
    }

    [Fact]
    public void MarkScanned_persists_summary_across_vm_recreation()
    {
        using var temp = new AppTempDb();
        var settings = new SettingsRepository(temp.Db);
        const string summary = "Added 7 · updated 0 · restored 0 · missing 3";

        new SettingsViewModel(settings).MarkScanned(summary);

        // Recreate the VM — it should reload the persisted summary.
        var vm2 = new SettingsViewModel(settings);
        vm2.LastScanSummaryText.ShouldBe(summary);
    }

    [Fact]
    public void MarkScanned_also_sets_LastScanText_to_non_never()
    {
        using var temp = new AppTempDb();
        var vm = new SettingsViewModel(new SettingsRepository(temp.Db));

        vm.MarkScanned("Added 1 · updated 0 · restored 0 · missing 0");

        vm.LastScanText.ShouldNotBe("Never scanned");
    }
}
