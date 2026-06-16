using System;
using System.IO;
using Shouldly;
using VideoShelf.App.Services;
using VideoShelf.App.ViewModels;
using Xunit;

namespace VideoShelf.App.Tests.Safety;

/// <summary>
/// B1 — pinning tests for the duplicate-resolve Recycle-Bin keeper gate.
///
/// The destroy-only-when-safe decision lives in the pure predicate
/// <see cref="DuplicateResolveViewModel.CanRecycleLosers(long)"/>: a loser may only be
/// recycled when the keeper file is present on disk AND has a non-zero byte length.
///
/// These tests exercise that predicate against a REAL <see cref="FileInfo"/> over a temp
/// directory (missing / zero-byte / present-and-non-empty), and assert via a recording
/// <see cref="FakeRecycleBinService"/> that NOTHING is recycled when the gate refuses.
///
/// (The full VM end-to-end recycle flow — DB-delete of losers, Resolved event, confirm gate —
/// is separately pinned in <c>DuplicateResolveViewModelTests</c>.)
/// </summary>
public sealed class RecycleBinKeeperGateTests : IDisposable
{
    private readonly string _dir;

    public RecycleBinKeeperGateTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "vs-keepergate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    /// <summary>Mirrors how the VM stats the keeper: real byte length, or -1 when missing.</summary>
    private static long RealLength(string path)
    {
        var fi = new FileInfo(path);
        return fi.Exists ? fi.Length : -1L;
    }

    // ── (a) keeper missing → refused, nothing recycled ───────────────────────

    [Fact]
    public void Refused_WhenKeeperMissing()
    {
        var keeperPath = Path.Combine(_dir, "keeper-does-not-exist.mp4");
        var loserPath  = Path.Combine(_dir, "loser.mp4");
        var bin = new FakeRecycleBinService();

        var len = RealLength(keeperPath);
        len.ShouldBe(-1L, "a missing keeper stats as -1");

        var allowed = DuplicateResolveViewModel.CanRecycleLosers(len);

        allowed.ShouldBeFalse("a missing keeper must block any recycle");
        if (allowed) bin.SendToRecycleBin(loserPath); // never reached when refused
        bin.Recycled.ShouldBeEmpty("nothing may be recycled when the keeper is missing");
    }

    // ── (b) keeper zero-bytes → refused, nothing recycled ────────────────────

    [Fact]
    public void Refused_WhenKeeperZeroBytes()
    {
        var keeperPath = Path.Combine(_dir, "keeper-empty.mp4");
        File.WriteAllBytes(keeperPath, Array.Empty<byte>()); // real zero-byte file
        var loserPath  = Path.Combine(_dir, "loser.mp4");
        var bin = new FakeRecycleBinService();

        var len = RealLength(keeperPath);
        len.ShouldBe(0L, "an empty file stats as 0 bytes");

        var allowed = DuplicateResolveViewModel.CanRecycleLosers(len);

        allowed.ShouldBeFalse("a zero-byte keeper must block any recycle");
        if (allowed) bin.SendToRecycleBin(loserPath);
        bin.Recycled.ShouldBeEmpty("nothing may be recycled when the keeper is zero bytes");
    }

    // ── (c) keeper present + non-empty → allowed ─────────────────────────────

    [Fact]
    public void Allowed_WhenKeeperPresentAndNonEmpty()
    {
        var keeperPath = Path.Combine(_dir, "keeper.mp4");
        File.WriteAllBytes(keeperPath, new byte[] { 1, 2, 3, 4 }); // real non-empty file
        var loserPath  = Path.Combine(_dir, "loser.mp4");
        var bin = new FakeRecycleBinService();

        var len = RealLength(keeperPath);
        len.ShouldBe(4L);

        var allowed = DuplicateResolveViewModel.CanRecycleLosers(len);

        allowed.ShouldBeTrue("a present, non-empty keeper allows recycling the loser");
        if (allowed) bin.SendToRecycleBin(loserPath);
        bin.Recycled.ShouldHaveSingleItem().ShouldBe(loserPath);
    }

    // ── predicate boundary ───────────────────────────────────────────────────

    [Theory]
    [InlineData(-1L, false)] // missing
    [InlineData(0L, false)]  // empty
    [InlineData(1L, true)]   // one byte
    [InlineData(long.MaxValue, true)]
    public void CanRecycleLosers_OnlyTrueForStrictlyPositiveLength(long length, bool expected)
        => DuplicateResolveViewModel.CanRecycleLosers(length).ShouldBe(expected);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }
}
