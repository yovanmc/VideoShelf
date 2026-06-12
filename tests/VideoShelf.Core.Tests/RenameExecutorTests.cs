// tests/VideoShelf.Core.Tests/RenameExecutorTests.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Shouldly;
using VideoShelf.Core.Models;
using VideoShelf.Core.Renaming;
using VideoShelf.Core.Storage;
using Xunit;

namespace VideoShelf.Core.Tests;

public class RenameExecutorTests : IDisposable
{
    private readonly string _dir;
    private readonly VideoShelfDb _db;
    private readonly LibraryRepository _library;
    private readonly long _seriesId;
    private readonly long _v1;
    private readonly long _v2;

    public RenameExecutorTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "vs-exec-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _db = new VideoShelfDb(Path.Combine(_dir, "library.db"));
        _db.Migrate();
        _library = new LibraryRepository(_db);
        var src = _library.UpsertSource(@"C:\root", "Root");
        var sec = _library.UpsertSection(src, "S");
        _seriesId = _library.UpsertSeries(sec, "Show", false);
        _v1 = _library.UpsertVideo(_seriesId, @"C:\m\old1.mkv", 1, "mkv");
        _v2 = _library.UpsertVideo(_seriesId, @"C:\m\old2.mkv", 2, "mkv");
    }

    private (RenameExecutor exec, InMemoryFileSystem fs, RenamePlan plan) Setup()
    {
        var fs = new InMemoryFileSystem(@"C:\m\old1.mkv", @"C:\m\old2.mkv");
        var planner = new RenamePlanner(fs);
        var videos = _library.GetVideosForSeries(_seriesId);
        var proposed = new Dictionary<long, string> { [_v1] = "Show 01.mkv", [_v2] = "Show 02.mkv" };
        var plan = planner.BuildPlan(videos, proposed);
        return (new RenameExecutor(fs, _library), fs, plan);
    }

    [Fact]
    public void Apply_RenamesFiles_RepathsDb_AndWritesManifest()
    {
        var (exec, fs, plan) = Setup();
        var result = exec.Apply(plan, _seriesId, Path.Combine(_dir, "manifests"));

        result.Renamed.ShouldBe(2);
        result.ManifestPath.ShouldNotBeNull();
        fs.FileExists(@"C:\m\Show 01.mkv").ShouldBeTrue();
        fs.FileExists(@"C:\m\old1.mkv").ShouldBeFalse();

        var paths = _library.GetVideosForSeries(_seriesId).Select(v => v.FilePath).OrderBy(p => p).ToArray();
        paths.ShouldBe(new[] { @"C:\m\Show 01.mkv", @"C:\m\Show 02.mkv" });
    }

    [Fact]
    public void Undo_ReversesFiles_AndRepathsDbBack()
    {
        var (exec, fs, plan) = Setup();
        var result = exec.Apply(plan, _seriesId, Path.Combine(_dir, "manifests"));

        var undo = exec.Undo(result.ManifestPath!);

        undo.Renamed.ShouldBe(2);
        fs.FileExists(@"C:\m\old1.mkv").ShouldBeTrue();
        fs.FileExists(@"C:\m\Show 01.mkv").ShouldBeFalse();
        var paths = _library.GetVideosForSeries(_seriesId).Select(v => v.FilePath).OrderBy(p => p).ToArray();
        paths.ShouldBe(new[] { @"C:\m\old1.mkv", @"C:\m\old2.mkv" });
    }

    [Fact]
    public void Undo_IsTolerant_OfEntriesWhoseMoveNeverHappened()
    {
        var (exec, fs, plan) = Setup();
        var result = exec.Apply(plan, _seriesId, Path.Combine(_dir, "manifests"));
        // Simulate a partial batch: delete one renamed file before undo.
        fs.Move(@"C:\m\Show 02.mkv", @"C:\m\somewhere-else.mkv");

        var undo = exec.Undo(result.ManifestPath!);

        // Only the still-present rename is reversed; the other is skipped, no throw.
        undo.Renamed.ShouldBe(1);
        fs.FileExists(@"C:\m\old1.mkv").ShouldBeTrue();
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }
}
