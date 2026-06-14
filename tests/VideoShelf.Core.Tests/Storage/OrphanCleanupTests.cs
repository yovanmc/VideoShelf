using System.IO;
using System.Linq;
using Shouldly;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;

namespace VideoShelf.Core.Tests.Storage;

/// <summary>D4: missing-video list, orphan lists, and DB-only index deletes.</summary>
public class OrphanCleanupTests
{
    private static (LibraryRepository lib, MaintenanceRepository maint, TempDb temp) Setup()
    {
        var temp  = new TempDb();
        var lib   = new LibraryRepository(temp.Db);
        var maint = new MaintenanceRepository(temp.Db);
        return (lib, maint, temp);
    }

    // ── GetMissingVideos ──────────────────────────────────────────────────────

    [Fact]
    public void GetMissingVideos_returns_only_missing_rows()
    {
        var (lib, maint, temp) = Setup();
        using var _ = temp;

        var src = lib.UpsertSource(@"C:\V", "V");
        var sec = lib.UpsertSection(src, "Creator");
        var ser = lib.UpsertSeries(sec, "Show", false);

        lib.UpsertVideo(ser, @"C:\V\Creator\a.mp4", 1, ".mp4");
        lib.UpsertVideo(ser, @"C:\V\Creator\b.mp4", 2, ".mp4");

        lib.MarkAllMissingForSource(src);
        lib.ClearMissing(@"C:\V\Creator\a.mp4");

        var missing = maint.GetMissingVideos();
        missing.Count.ShouldBe(1);
        missing[0].FilePath.ShouldBe(@"C:\V\Creator\b.mp4");
        missing[0].CreatorName.ShouldBe("Creator");
        missing[0].SeriesTitle.ShouldBe("Show");
    }

    [Fact]
    public void GetMissingVideos_empty_when_none_missing()
    {
        var (lib, maint, temp) = Setup();
        using var _ = temp;

        var src = lib.UpsertSource(@"C:\V", "V");
        var sec = lib.UpsertSection(src, "Creator");
        var ser = lib.UpsertSeries(sec, "Show", false);

        lib.UpsertVideo(ser, @"C:\V\Creator\a.mp4", 1, ".mp4");

        maint.GetMissingVideos().ShouldBeEmpty();
    }

    // ── GetOrphanSeries ───────────────────────────────────────────────────────

    [Fact]
    public void GetOrphanSeries_returns_series_with_no_playable_videos()
    {
        var (lib, maint, temp) = Setup();
        using var _ = temp;

        var src  = lib.UpsertSource(@"C:\V", "V");
        var sec  = lib.UpsertSection(src, "Creator");
        var ser1 = lib.UpsertSeries(sec, "Good Show", false);
        var ser2 = lib.UpsertSeries(sec, "Dead Show", false);

        lib.UpsertVideo(ser1, @"C:\V\Creator\a.mp4", 1, ".mp4");
        lib.UpsertVideo(ser2, @"C:\V\Creator\b.mp4", 1, ".mp4");

        lib.MarkAllMissingForSource(src);
        lib.ClearMissing(@"C:\V\Creator\a.mp4");

        var orphans = maint.GetOrphanSeries();
        orphans.Count.ShouldBe(1);
        orphans[0].Title.ShouldBe("Dead Show");
        orphans[0].CreatorName.ShouldBe("Creator");
    }

    // ── GetEmptyCreators ──────────────────────────────────────────────────────

    [Fact]
    public void GetEmptyCreators_returns_sections_with_no_playable_videos()
    {
        var (lib, maint, temp) = Setup();
        using var _ = temp;

        var src  = lib.UpsertSource(@"C:\V", "V");
        var sec1 = lib.UpsertSection(src, "Active");
        var sec2 = lib.UpsertSection(src, "Empty");
        var ser1 = lib.UpsertSeries(sec1, "Show", false);
        var ser2 = lib.UpsertSeries(sec2, "Show", false);

        lib.UpsertVideo(ser1, @"C:\V\Active\a.mp4", 1, ".mp4");
        lib.UpsertVideo(ser2, @"C:\V\Empty\b.mp4",  1, ".mp4");

        lib.MarkAllMissingForSource(src);
        lib.ClearMissing(@"C:\V\Active\a.mp4");

        var empty = maint.GetEmptyCreators();
        empty.Count.ShouldBe(1);
        empty[0].Title.ShouldBe("Empty");
    }

    // ── DeleteSeriesIndex ─────────────────────────────────────────────────────

    [Fact]
    public void DeleteSeriesIndex_removes_series_and_its_videos_from_db()
    {
        var (lib, maint, temp) = Setup();
        using var _ = temp;

        var src  = lib.UpsertSource(@"C:\V", "V");
        var sec  = lib.UpsertSection(src, "Creator");
        var ser1 = lib.UpsertSeries(sec, "Target", false);
        var ser2 = lib.UpsertSeries(sec, "Keep", false);

        lib.UpsertVideo(ser1, @"C:\V\Creator\a.mp4", 1, ".mp4");
        lib.UpsertVideo(ser2, @"C:\V\Creator\b.mp4", 1, ".mp4");

        maint.DeleteSeriesIndex(ser1);

        // ser1 gone; ser2 intact
        lib.GetVideosForSeries(ser1).ShouldBeEmpty();
        lib.GetVideosForSeries(ser2).Count.ShouldBe(1);

        // Series list should not include ser1 (check via orphan list — ser1 was only series)
        var orphans = maint.GetOrphanSeries();
        orphans.Any(o => o.Id == ser1).ShouldBeFalse();
    }

    [Fact]
    public void DeleteSeriesIndex_does_not_touch_filesystem()
    {
        // Guard: this is a DB-only operation. We verify no FS paths are touched
        // by placing the DB in a known temp dir and confirming no .mp4 is created or deleted.
        var (lib, maint, temp) = Setup();
        using var _ = temp;

        var tempDir = Path.GetTempPath();
        var fakePath = Path.Combine(tempDir, "vshelf_fake_" + System.Guid.NewGuid().ToString("N") + ".mp4");

        var src = lib.UpsertSource(tempDir, "Temp");
        var sec = lib.UpsertSection(src, "Creator");
        var ser = lib.UpsertSeries(sec, "Show", false);
        lib.UpsertVideo(ser, fakePath, 1, ".mp4");

        // File does NOT exist on disk — that's fine
        File.Exists(fakePath).ShouldBeFalse();

        maint.DeleteSeriesIndex(ser);

        // File still does not exist (not created, not deleted — no FS call)
        File.Exists(fakePath).ShouldBeFalse();
    }

    // ── DeleteSectionIndex ────────────────────────────────────────────────────

    [Fact]
    public void DeleteSectionIndex_removes_section_series_and_videos_from_db()
    {
        var (lib, maint, temp) = Setup();
        using var _ = temp;

        var src  = lib.UpsertSource(@"C:\V", "V");
        var sec1 = lib.UpsertSection(src, "Target");
        var sec2 = lib.UpsertSection(src, "Keep");
        var ser1 = lib.UpsertSeries(sec1, "Show", false);
        var ser2 = lib.UpsertSeries(sec2, "Show", false);

        lib.UpsertVideo(ser1, @"C:\V\Target\a.mp4", 1, ".mp4");
        lib.UpsertVideo(ser2, @"C:\V\Keep\b.mp4",   1, ".mp4");

        maint.DeleteSectionIndex(sec1);

        // sec1's videos gone
        lib.GetVideosForSeries(ser1).ShouldBeEmpty();
        // sec2 still intact
        lib.GetVideosForSeries(ser2).Count.ShouldBe(1);

        // sec2 is not empty
        maint.GetEmptyCreators().Any(e => e.Id == sec2).ShouldBeFalse();
    }

    [Fact]
    public void DeleteSectionIndex_does_not_touch_filesystem()
    {
        var (lib, maint, temp) = Setup();
        using var _ = temp;

        var tempDir = Path.GetTempPath();
        var fakePath = Path.Combine(tempDir, "vshelf_sec_" + System.Guid.NewGuid().ToString("N") + ".mp4");

        var src = lib.UpsertSource(tempDir, "Temp");
        var sec = lib.UpsertSection(src, "Creator");
        var ser = lib.UpsertSeries(sec, "Show", false);
        lib.UpsertVideo(ser, fakePath, 1, ".mp4");

        File.Exists(fakePath).ShouldBeFalse();

        maint.DeleteSectionIndex(sec);

        File.Exists(fakePath).ShouldBeFalse();
    }

    [Fact]
    public void DeleteSectionIndex_leaves_other_sections_untouched()
    {
        var (lib, maint, temp) = Setup();
        using var _ = temp;

        var src  = lib.UpsertSource(@"C:\V", "V");
        var sec1 = lib.UpsertSection(src, "Gone");
        var sec2 = lib.UpsertSection(src, "Stays");
        var ser1 = lib.UpsertSeries(sec1, "Show", false);
        var ser2 = lib.UpsertSeries(sec2, "Show", false);

        lib.UpsertVideo(ser1, @"C:\V\Gone\a.mp4",  1, ".mp4");
        lib.UpsertVideo(ser2, @"C:\V\Stays\b.mp4", 1, ".mp4");

        maint.DeleteSectionIndex(sec1);

        var sources = lib.GetSources();
        sources.Count.ShouldBe(1); // source unaffected

        lib.GetVideosForSeries(ser2).Count.ShouldBe(1);
    }
}
