// tests/VideoShelf.Core.Tests/UpdateVideoPathTests.cs
using System;
using System.IO;
using System.Linq;
using Shouldly;
using VideoShelf.Core.Storage;
using Xunit;

namespace VideoShelf.Core.Tests;

public class UpdateVideoPathTests : IDisposable
{
    private readonly string _dir;
    private readonly VideoShelfDb _db;
    private readonly LibraryRepository _library;
    private readonly WatchRepository _watch;

    public UpdateVideoPathTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "vs-rename-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _db = new VideoShelfDb(Path.Combine(_dir, "library.db"));
        _db.Migrate();
        _library = new LibraryRepository(_db);
        _watch = new WatchRepository(_db);
    }

    [Fact]
    public void RepathsFilePathAndRawFilename_AndStateSurvives()
    {
        var src = _library.UpsertSource(@"C:\root", "Root");
        var sec = _library.UpsertSection(src, "Section");
        var ser = _library.UpsertSeries(sec, "Show", isStandalone: false);
        var vid = _library.UpsertVideo(ser, @"C:\root\Section\old1.mkv", 1, "mkv");

        _watch.SetWatched(vid, true);
        _library.SetResumePosition(vid, 123.0);

        _library.UpdateVideoPath(vid, @"C:\root\Section\old1.mkv", @"C:\root\Section\Show 01.mkv");

        var v = _library.GetVideosForSeries(ser).Single();
        v.FilePath.ShouldBe(@"C:\root\Section\Show 01.mkv");
        v.RawFilename.ShouldBe("Show 01.mkv");
        _watch.IsWatched(vid).ShouldBeTrue();
        _library.GetResumePosition(vid).ShouldBe(123.0);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }
}
