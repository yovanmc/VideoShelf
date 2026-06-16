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
    public async Task Load_BuildsCanonicalEditableProposal_SingleFile()
    {
        var vm = Build();
        await vm.LoadAsync(_seriesId, "My Show", isStandalone: false);

        // Single-file rename: only the first episode is shown, episode number preserved.
        vm.Rows.Count.ShouldBe(1);
        vm.Rows[0].NewName.ShouldBe("My Show 01.mkv");
        vm.Rows[0].WillRename.ShouldBeTrue();
    }

    [Fact]
    public async Task Load_WithVideoId_TargetsThatEpisode_AndPreservesEpisodeNumber()
    {
        // Seed: two-episode series — UpsertVideo returns the video id for ep2.
        var ep2Id = _library.GetVideosForSeries(_seriesId)
                             .First(v => v.EpisodeNo == 2).Id;

        var vm = Build();
        await vm.LoadAsync(_seriesId, "My Show", isStandalone: false, videoId: ep2Id);

        // Must target episode 2, not episode 1.
        vm.Rows.Count.ShouldBe(1);
        vm.Rows[0].VideoId.ShouldBe(ep2Id);
        // Episode number 2 must be preserved, pad-width = 2 (max ep = 2).
        vm.Rows[0].NewName.ShouldBe("My Show 02.mkv");
        vm.Rows[0].WillRename.ShouldBeTrue();
    }

    [Fact]
    public async Task Load_Standalone_ProposesNoEpisodeNumber()
    {
        // Create a standalone series with a single video.
        var src = _library.UpsertSource(@"C:\standalone", "Standalone");
        var sec = _library.UpsertSection(src, "Sec");
        var standaloneSeries = _library.UpsertSeries(sec, "Big Buck Bunny", isStandalone: true);
        _library.UpsertVideo(standaloneSeries, @"C:\standalone\bbb_junk.mkv", 1, "mkv");
        _fs.AddFile(@"C:\standalone\bbb_junk.mkv");

        var vm = Build();
        await vm.LoadAsync(standaloneSeries, "Big Buck Bunny", isStandalone: true);

        vm.Rows.Count.ShouldBe(1);
        // Standalone → no episode number suffix.
        vm.Rows[0].NewName.ShouldBe("Big Buck Bunny.mkv");
    }

    [Fact]
    public async Task Apply_RenamesOnDisk_RepathsDb_AndEnablesUndo()
    {
        var vm = Build();
        await vm.LoadAsync(_seriesId, "My Show", false);
        await vm.ApplyCommand.ExecuteAsync(null);

        // Episode 1 of 2 → padded "My Show 01.mkv"; ep2 is untouched.
        _fs.FileExists(@"C:\m\My Show 01.mkv").ShouldBeTrue();
        var paths = _library.GetVideosForSeries(_seriesId).Select(v => Path.GetFileName(v.FilePath)).ToList();
        paths.ShouldContain("My Show 01.mkv");
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
