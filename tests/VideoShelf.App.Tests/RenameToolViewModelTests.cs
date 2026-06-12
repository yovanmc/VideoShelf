// tests/VideoShelf.App.Tests/RenameToolViewModelTests.cs
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Shouldly;
using VideoShelf.App.Services;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Renaming;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests;   // InMemoryFileSystem (see note below)
using Xunit;

namespace VideoShelf.App.Tests;

public class RenameToolViewModelTests : IDisposable
{
    private readonly string _dir;
    private readonly VideoShelfDb _db;
    private readonly LibraryRepository _library;
    private readonly SettingsRepository _settings;
    private readonly InMemoryFileSystem _fs;
    private readonly long _seriesId;

    public RenameToolViewModelTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "vs-vm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _db = new VideoShelfDb(Path.Combine(_dir, "library.db"));
        _db.Migrate();
        _library = new LibraryRepository(_db);
        _settings = new SettingsRepository(_db);
        var src = _library.UpsertSource(@"C:\root", "Root");
        var sec = _library.UpsertSection(src, "S");
        _seriesId = _library.UpsertSeries(sec, "My Show", false);
        _library.UpsertVideo(_seriesId, @"C:\m\junk_ep1.mkv", 1, "mkv");
        _library.UpsertVideo(_seriesId, @"C:\m\junk_ep2.mkv", 2, "mkv");
        _fs = new InMemoryFileSystem(@"C:\m\junk_ep1.mkv", @"C:\m\junk_ep2.mkv");
    }

    private RenameToolViewModel Build()
    {
        var planner = new RenamePlanner(_fs);
        var executor = new RenameExecutor(_fs, _library);
        var paths = new AppPaths(_dir);
        return new RenameToolViewModel(_library, planner, executor, _settings, paths);
    }

    [Fact]
    public async Task Load_BuildsCanonicalEditableProposals()
    {
        var vm = Build();
        await vm.LoadAsync(_seriesId, "My Show", isStandalone: false);

        vm.Rows.Count.ShouldBe(2);
        vm.Rows.Select(r => r.NewName).ShouldBe(new[] { "My Show 01.mkv", "My Show 02.mkv" });
        vm.Rows.All(r => r.WillRename).ShouldBeTrue();
    }

    [Fact]
    public async Task EditingName_ReplansAndFlagsDuplicate()
    {
        var vm = Build();
        await vm.LoadAsync(_seriesId, "My Show", false);
        vm.Rows[1].NewName = "My Show 01.mkv"; // collide with row 0

        vm.Rows[0].Status.ShouldBe(RenameItemStatus.DuplicateTarget);
        vm.Rows[1].Status.ShouldBe(RenameItemStatus.DuplicateTarget);
    }

    [Fact]
    public async Task Apply_RenamesOnDisk_RepathsDb_AndEnablesUndo()
    {
        var vm = Build();
        await vm.LoadAsync(_seriesId, "My Show", false);
        await vm.ApplyCommand.ExecuteAsync(null);

        _fs.FileExists(@"C:\m\My Show 01.mkv").ShouldBeTrue();
        _library.GetVideosForSeries(_seriesId).Select(v => Path.GetFileName(v.FilePath))
            .OrderBy(n => n).ShouldBe(new[] { "My Show 01.mkv", "My Show 02.mkv" });
        vm.CanUndo.ShouldBeTrue();
    }

    [Fact]
    public async Task Undo_RevertsDiskAndDb()
    {
        var vm = Build();
        await vm.LoadAsync(_seriesId, "My Show", false);
        await vm.ApplyCommand.ExecuteAsync(null);
        await vm.UndoCommand.ExecuteAsync(null);

        _fs.FileExists(@"C:\m\junk_ep1.mkv").ShouldBeTrue();
        vm.CanUndo.ShouldBeFalse();
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_dir, true); } catch { }
    }
}
