using System;
using Shouldly;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;

namespace VideoShelf.Core.Tests.Storage;

/// <summary>D3: MaintenanceSummary counts and DB-size field.</summary>
public class MaintenanceSummaryTests
{
    private static (LibraryRepository lib, MaintenanceRepository maint, TempDb temp) Setup()
    {
        var temp  = new TempDb();
        var lib   = new LibraryRepository(temp.Db);
        var maint = new MaintenanceRepository(temp.Db);
        return (lib, maint, temp);
    }

    [Fact]
    public void Empty_db_gives_all_zeros_and_positive_db_size()
    {
        var (_, maint, temp) = Setup();
        using var _ = temp;

        var s = maint.GetMaintenanceSummary();

        s.MissingCount.ShouldBe(0);
        s.OrphanSeriesCount.ShouldBe(0);
        s.EmptyCreatorCount.ShouldBe(0);
        s.SingleFileSeriesCount.ShouldBe(0);
        s.DuplicateGroupCount.ShouldBe(0);
        s.DbSizeBytes.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Missing_count_reflects_marked_videos()
    {
        var (lib, maint, temp) = Setup();
        using var _ = temp;

        var src = lib.UpsertSource(@"C:\V", "V");
        var sec = lib.UpsertSection(src, "Creator");
        var ser = lib.UpsertSeries(sec, "Show", false);

        lib.UpsertVideo(ser, @"C:\V\Creator\a.mp4", 1, ".mp4");
        lib.UpsertVideo(ser, @"C:\V\Creator\b.mp4", 2, ".mp4");

        lib.MarkAllMissingForSource(src);

        var s = maint.GetMaintenanceSummary();
        s.MissingCount.ShouldBe(2);
    }

    [Fact]
    public void Orphan_series_count_is_series_with_only_missing_videos()
    {
        var (lib, maint, temp) = Setup();
        using var _ = temp;

        var src  = lib.UpsertSource(@"C:\V", "V");
        var sec  = lib.UpsertSection(src, "Creator");
        var ser1 = lib.UpsertSeries(sec, "Show A", false); // will have a present video
        var ser2 = lib.UpsertSeries(sec, "Show B", false); // all missing

        lib.UpsertVideo(ser1, @"C:\V\Creator\a.mp4", 1, ".mp4");
        lib.UpsertVideo(ser2, @"C:\V\Creator\b.mp4", 1, ".mp4");

        lib.MarkAllMissingForSource(src);
        // Restore ser1's video
        lib.ClearMissing(@"C:\V\Creator\a.mp4");

        var s = maint.GetMaintenanceSummary();
        s.OrphanSeriesCount.ShouldBe(1); // only ser2
    }

    [Fact]
    public void Empty_creator_count_is_section_with_no_playable_videos()
    {
        var (lib, maint, temp) = Setup();
        using var _ = temp;

        var src  = lib.UpsertSource(@"C:\V", "V");
        var sec1 = lib.UpsertSection(src, "GoodCreator");
        var sec2 = lib.UpsertSection(src, "EmptyCreator");
        var ser1 = lib.UpsertSeries(sec1, "Show", false);
        var ser2 = lib.UpsertSeries(sec2, "Show", false);

        lib.UpsertVideo(ser1, @"C:\V\GoodCreator\a.mp4", 1, ".mp4");
        lib.UpsertVideo(ser2, @"C:\V\EmptyCreator\b.mp4", 1, ".mp4");

        lib.MarkAllMissingForSource(src);
        lib.ClearMissing(@"C:\V\GoodCreator\a.mp4");

        var s = maint.GetMaintenanceSummary();
        s.EmptyCreatorCount.ShouldBe(1); // sec2 has no playable video
    }

    [Fact]
    public void Single_file_series_count()
    {
        var (lib, maint, temp) = Setup();
        using var _ = temp;

        var src  = lib.UpsertSource(@"C:\V", "V");
        var sec  = lib.UpsertSection(src, "Creator");
        var ser1 = lib.UpsertSeries(sec, "Standalone", true);  // 1 file → single-file
        var ser2 = lib.UpsertSeries(sec, "Multi", false);       // 2 files → not single-file

        lib.UpsertVideo(ser1, @"C:\V\Creator\a.mp4", 1, ".mp4");
        lib.UpsertVideo(ser2, @"C:\V\Creator\b.mp4", 1, ".mp4");
        lib.UpsertVideo(ser2, @"C:\V\Creator\c.mp4", 2, ".mp4");

        var s = maint.GetMaintenanceSummary();
        s.SingleFileSeriesCount.ShouldBe(1);
    }

    [Fact]
    public void Duplicate_group_count_reflects_detected_groups()
    {
        var (lib, maint, temp) = Setup();
        using var _ = temp;

        var src  = lib.UpsertSource(@"C:\V", "V");
        var sec  = lib.UpsertSection(src, "Creator");
        var ser1 = lib.UpsertSeries(sec, "A", false);
        var ser2 = lib.UpsertSeries(sec, "B", false);
        var ser3 = lib.UpsertSeries(sec, "C2", false);
        var ser4 = lib.UpsertSeries(sec, "D", false);

        // Group 1: size=100, dur=30
        lib.UpsertVideo(ser1, @"C:\V\Creator\a.mp4", 1, ".mp4", sizeBytes: 100);
        lib.UpsertVideo(ser2, @"C:\V\Creator\b.mp4", 1, ".mp4", sizeBytes: 100);
        var v1 = lib.GetVideosForSeries(ser1).Single();
        var v2 = lib.GetVideosForSeries(ser2).Single();
        lib.SetDuration(v1.Id, 30.0);
        lib.SetDuration(v2.Id, 30.0);

        // Group 2: size=999, dur=99
        lib.UpsertVideo(ser3, @"C:\V\Creator\c.mp4", 1, ".mp4", sizeBytes: 999);
        lib.UpsertVideo(ser4, @"C:\V\Creator\d.mp4", 1, ".mp4", sizeBytes: 999);
        var v3 = lib.GetVideosForSeries(ser3).Single();
        var v4 = lib.GetVideosForSeries(ser4).Single();
        lib.SetDuration(v3.Id, 99.0);
        lib.SetDuration(v4.Id, 99.0);

        var s = maint.GetMaintenanceSummary();
        s.DuplicateGroupCount.ShouldBe(2);
    }

    [Fact]
    public void Db_size_is_positive()
    {
        var (_, maint, temp) = Setup();
        using var _ = temp;

        var s = maint.GetMaintenanceSummary();
        s.DbSizeBytes.ShouldBeGreaterThan(0);
    }
}
