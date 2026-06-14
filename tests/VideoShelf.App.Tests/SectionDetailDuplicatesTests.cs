using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Shouldly;
using VideoShelf.App.Services;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests;
using Xunit;

namespace VideoShelf.App.Tests;

/// <summary>
/// M18-G tests for the duplicate-group surfacing on <see cref="SectionDetailViewModel"/>:
///   - PossibleDuplicates is populated after LoadAsync when duplicates exist.
///   - HasDuplicates reflects the count.
///   - After NotADuplicate dismiss, the group no longer surfaces (RefreshDuplicates removes it).
///   - OpenResolveCommand raises ResolveRequested with a correctly-typed VM.
/// </summary>
public sealed class SectionDetailDuplicatesTests
{
    // ── Fixture ───────────────────────────────────────────────────────────────

    private sealed class NullThumbs : IThumbnailService
    {
        public Task<string?> GetThumbnailPathAsync(string videoPath, CancellationToken ct)
            => Task.FromResult<string?>(null);
    }

    private sealed record Fx(
        AppTempDb TempDb,
        LibraryRepository Lib,
        MaintenanceRepository Maintenance,
        SectionDetailViewModel Vm,
        FakeRecycleBinService RecycleBin,
        FakeConfirmService Confirm,
        InMemoryFileSystem Fs,
        long SectionId,
        long VideoIdA,
        long VideoIdB,
        string PathA,
        string PathB);

    private static Fx NewFx()
    {
        var db       = new AppTempDb();
        var lib      = new LibraryRepository(db.Db);
        var tags     = new TagRepository(db.Db);
        var watch    = new WatchRepository(db.Db);
        var art      = new CreatorArtRepository(db.Db);
        var maint    = new MaintenanceRepository(db.Db);
        var settings = new SettingsRepository(db.Db);

        var srcId    = lib.UpsertSource(@"C:\V", "V");
        var secId    = lib.UpsertSection(srcId, "Creator");
        var seriesId = lib.UpsertSeries(secId, "Show", false);

        const string pathA = @"C:\V\Creator\Show\ep01.mp4";
        const string pathB = @"C:\V\Creator\Show\ep01_copy.mp4";

        var vidA = lib.UpsertVideo(seriesId, pathA, 1, ".mp4");
        var vidB = lib.UpsertVideo(seriesId, pathB, 2, ".mp4");

        SetSizeAndDuration(db, vidA, 200_000_000L, 90.0);
        SetSizeAndDuration(db, vidB, 200_000_000L, 90.0);

        var recycleBin = new FakeRecycleBinService();
        var confirm    = new FakeConfirmService { NextResult = true };
        var fs         = new InMemoryFileSystem(pathA, pathB);
        fs.AddFile(pathA, "data");
        fs.AddFile(pathB, "data_copy");

        var playQueue = new PlayQueueViewModel(lib, settings);
        var vm = new SectionDetailViewModel(
            lib, tags, watch, new NullThumbs(), art,
            new FakeImagePicker(null), playQueue,
            maintenance: maint,
            recycleBin: recycleBin,
            confirm: confirm,
            fs: fs);

        return new Fx(db, lib, maint, vm, recycleBin, confirm, fs, secId, vidA, vidB, pathA, pathB);
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

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LoadAsync_PopulatesPossibleDuplicates_WhenDuplicatesExist()
    {
        var f = NewFx(); using var _ = f.TempDb;
        await f.Vm.LoadAsync(f.SectionId);

        f.Vm.PossibleDuplicates.Count.ShouldBe(1, "one duplicate group seeded");
        f.Vm.HasDuplicates.ShouldBeTrue();
    }

    [Fact]
    public async Task LoadAsync_PossibleDuplicates_Empty_WhenNoDuplicates()
    {
        var f = NewFx(); using var _ = f.TempDb;

        // Remove one video so they're no longer duplicate candidates.
        f.Maintenance.DeleteSectionIndex(f.SectionId);

        // Recreate a section with just one video (no duplicates).
        var srcId    = f.Lib.UpsertSource(@"C:\V2", "V2");
        var secId2   = f.Lib.UpsertSection(srcId, "Solo Creator");
        var seriesId = f.Lib.UpsertSeries(secId2, "Solo Show", false);
        f.Lib.UpsertVideo(seriesId, @"C:\V2\Solo Creator\Solo Show\ep01.mp4", 1, ".mp4");

        await f.Vm.LoadAsync(secId2);

        f.Vm.PossibleDuplicates.ShouldBeEmpty();
        f.Vm.HasDuplicates.ShouldBeFalse();
    }

    [Fact]
    public async Task OpenResolveCommand_RaisesResolveRequested_WithCorrectVm()
    {
        var f = NewFx(); using var _ = f.TempDb;
        await f.Vm.LoadAsync(f.SectionId);

        DuplicateResolveViewModel? resolveVm = null;
        f.Vm.ResolveRequested += (_, vm) => resolveVm = vm;

        var group = f.Vm.PossibleDuplicates[0];
        f.Vm.OpenResolveCommand.Execute(group);

        resolveVm.ShouldNotBeNull();
        resolveVm!.Candidates.Count.ShouldBe(2);
    }

    [Fact]
    public async Task AfterDismiss_PossibleDuplicates_Clears()
    {
        var f = NewFx(); using var _ = f.TempDb;
        await f.Vm.LoadAsync(f.SectionId);

        // Capture the resolve VM via ResolveRequested.
        DuplicateResolveViewModel? resolveVm = null;
        f.Vm.ResolveRequested += (_, vm) => resolveVm = vm;

        var group = f.Vm.PossibleDuplicates[0];
        f.Vm.OpenResolveCommand.Execute(group);

        resolveVm.ShouldNotBeNull();

        // Dismiss via the resolve VM's NotADuplicate command.
        resolveVm!.NotADuplicateCommand.Execute(null);

        // The Resolved event from the resolveVm triggers RefreshDuplicates on the section detail VM.
        f.Vm.PossibleDuplicates.ShouldBeEmpty("dismissed pair must be removed from the banner");
        f.Vm.HasDuplicates.ShouldBeFalse();
    }

    [Fact]
    public async Task AfterKeep_PossibleDuplicates_Clears()
    {
        var f = NewFx(); using var _ = f.TempDb;
        await f.Vm.LoadAsync(f.SectionId);

        // Ensure path A is present and non-zero so the safety gate passes.
        f.Fs.GetFileLength(f.PathA).ShouldBeGreaterThan(0);

        DuplicateResolveViewModel? resolveVm = null;
        f.Vm.ResolveRequested += (_, vm) => resolveVm = vm;

        var group = f.Vm.PossibleDuplicates[0];
        f.Vm.OpenResolveCommand.Execute(group);

        // Keep path A (first candidate).
        var keeperRow = resolveVm!.Candidates.First(c => c.FilePath == f.PathA);
        keeperRow.KeepCommand.Execute(null);

        // Loser B recycled.
        f.RecycleBin.Recycled.ShouldContain(f.PathB);

        // After Resolved fires → RefreshDuplicates → PossibleDuplicates cleared.
        f.Vm.PossibleDuplicates.ShouldBeEmpty("after resolve, no more duplicates for this section");
    }
}
