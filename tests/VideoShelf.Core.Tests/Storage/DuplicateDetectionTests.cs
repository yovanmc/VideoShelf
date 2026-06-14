using System;
using System.Linq;
using Shouldly;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;

namespace VideoShelf.Core.Tests.Storage;

/// <summary>D1 + D2: duplicate grouping logic and dismissal filtering.</summary>
public class DuplicateDetectionTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static (LibraryRepository lib, MaintenanceRepository maint, TempDb temp) Setup()
    {
        var temp = new TempDb();
        var lib  = new LibraryRepository(temp.Db);
        var maint = new MaintenanceRepository(temp.Db);
        return (lib, maint, temp);
    }

    private static long AddVideo(LibraryRepository lib, long seriesId, string path,
                                  long? sizeBytes = null, double? duration = null,
                                  int? width = null, int? height = null)
    {
        lib.UpsertVideo(seriesId, path, episodeNo: 1, format: ".mp4", sizeBytes: sizeBytes);
        // Retrieve the id via path
        var videos = lib.GetVideosForSeries(seriesId);
        var v = videos.Single(x => x.FilePath == path);
        if (duration.HasValue)
            lib.SetDuration(v.Id, duration.Value);
        if (width.HasValue && height.HasValue)
            lib.SetResolution(v.Id, width.Value, height.Value);
        return v.Id;
    }

    // ── D1: grouping ─────────────────────────────────────────────────────────

    [Fact]
    public void Same_size_and_duration_yields_one_group()
    {
        var (lib, maint, temp) = Setup();
        using var _ = temp;

        var src  = lib.UpsertSource(@"C:\V", "V");
        var sec  = lib.UpsertSection(src, "Creator");
        var ser1 = lib.UpsertSeries(sec, "Show A", false);
        var ser2 = lib.UpsertSeries(sec, "Show B", false);

        AddVideo(lib, ser1, @"C:\V\Creator\a.mp4", sizeBytes: 1000, duration: 60.1);
        AddVideo(lib, ser2, @"C:\V\Creator\b.mp4", sizeBytes: 1000, duration: 60.4);

        var groups = maint.GetDuplicateGroups();
        groups.Count.ShouldBe(1);
        groups[0].SizeBytes.ShouldBe(1000L);
        groups[0].DurationRoundedSeconds.ShouldBe(60);
        groups[0].Videos.Count.ShouldBe(2);
    }

    [Fact]
    public void Different_size_does_not_group()
    {
        var (lib, maint, temp) = Setup();
        using var _ = temp;

        var src  = lib.UpsertSource(@"C:\V", "V");
        var sec  = lib.UpsertSection(src, "Creator");
        var ser1 = lib.UpsertSeries(sec, "Show A", false);
        var ser2 = lib.UpsertSeries(sec, "Show B", false);

        AddVideo(lib, ser1, @"C:\V\Creator\a.mp4", sizeBytes: 1000, duration: 60.0);
        AddVideo(lib, ser2, @"C:\V\Creator\b.mp4", sizeBytes: 2000, duration: 60.0);

        maint.GetDuplicateGroups().ShouldBeEmpty();
    }

    [Fact]
    public void Different_duration_does_not_group()
    {
        var (lib, maint, temp) = Setup();
        using var _ = temp;

        var src  = lib.UpsertSource(@"C:\V", "V");
        var sec  = lib.UpsertSection(src, "Creator");
        var ser1 = lib.UpsertSeries(sec, "Show A", false);
        var ser2 = lib.UpsertSeries(sec, "Show B", false);

        AddVideo(lib, ser1, @"C:\V\Creator\a.mp4", sizeBytes: 1000, duration: 60.0);
        AddVideo(lib, ser2, @"C:\V\Creator\b.mp4", sizeBytes: 1000, duration: 61.0);

        maint.GetDuplicateGroups().ShouldBeEmpty();
    }

    [Fact]
    public void Missing_videos_excluded_from_duplicate_groups()
    {
        var (lib, maint, temp) = Setup();
        using var _ = temp;

        var src  = lib.UpsertSource(@"C:\V", "V");
        var sec  = lib.UpsertSection(src, "Creator");
        var ser1 = lib.UpsertSeries(sec, "Show A", false);
        var ser2 = lib.UpsertSeries(sec, "Show B", false);

        AddVideo(lib, ser1, @"C:\V\Creator\a.mp4", sizeBytes: 1000, duration: 60.0);
        var id2 = AddVideo(lib, ser2, @"C:\V\Creator\b.mp4", sizeBytes: 1000, duration: 60.0);

        // Mark b.mp4 as missing
        lib.MarkAllMissingForSource(lib.GetSources()[0].Id);

        maint.GetDuplicateGroups().ShouldBeEmpty();
    }

    [Fact]
    public void Videos_with_null_size_or_duration_excluded()
    {
        var (lib, maint, temp) = Setup();
        using var _ = temp;

        var src  = lib.UpsertSource(@"C:\V", "V");
        var sec  = lib.UpsertSection(src, "Creator");
        var ser1 = lib.UpsertSeries(sec, "Show A", false);
        var ser2 = lib.UpsertSeries(sec, "Show B", false);

        // Only size, no duration
        AddVideo(lib, ser1, @"C:\V\Creator\a.mp4", sizeBytes: 1000, duration: null);
        AddVideo(lib, ser2, @"C:\V\Creator\b.mp4", sizeBytes: 1000, duration: null);

        maint.GetDuplicateGroups().ShouldBeEmpty();
    }

    [Fact]
    public void Section_scoped_returns_only_that_section()
    {
        var (lib, maint, temp) = Setup();
        using var _ = temp;

        var src  = lib.UpsertSource(@"C:\V", "V");
        var sec1 = lib.UpsertSection(src, "CreatorA");
        var sec2 = lib.UpsertSection(src, "CreatorB");
        var ser1 = lib.UpsertSeries(sec1, "Show", false);
        var ser2 = lib.UpsertSeries(sec1, "Show2", false);
        var ser3 = lib.UpsertSeries(sec2, "Show", false);
        var ser4 = lib.UpsertSeries(sec2, "Show2", false);

        // Duplicates within sec1
        AddVideo(lib, ser1, @"C:\V\CreatorA\a.mp4", sizeBytes: 500, duration: 30.0);
        AddVideo(lib, ser2, @"C:\V\CreatorA\b.mp4", sizeBytes: 500, duration: 30.0);
        // Duplicates within sec2 (different size/duration)
        AddVideo(lib, ser3, @"C:\V\CreatorB\c.mp4", sizeBytes: 999, duration: 99.0);
        AddVideo(lib, ser4, @"C:\V\CreatorB\d.mp4", sizeBytes: 999, duration: 99.0);

        var forSec1 = maint.GetDuplicateGroupsForSection(sec1);
        forSec1.Count.ShouldBe(1);
        forSec1[0].Videos.All(v => v.SectionId == sec1).ShouldBeTrue();

        var forSec2 = maint.GetDuplicateGroupsForSection(sec2);
        forSec2.Count.ShouldBe(1);
        forSec2[0].Videos.All(v => v.SectionId == sec2).ShouldBeTrue();
    }

    [Fact]
    public void Duration_rounded_to_nearest_second_for_grouping()
    {
        var (lib, maint, temp) = Setup();
        using var _ = temp;

        var src = lib.UpsertSource(@"C:\V", "V");
        var sec = lib.UpsertSection(src, "C");
        var s1  = lib.UpsertSeries(sec, "A", false);
        var s2  = lib.UpsertSeries(sec, "B", false);

        // 60.1 and 60.4 both round to 60; 60.6 rounds to 61
        AddVideo(lib, s1, @"C:\V\C\a.mp4", sizeBytes: 100, duration: 60.1);
        AddVideo(lib, s2, @"C:\V\C\b.mp4", sizeBytes: 100, duration: 60.4);

        var groups = maint.GetDuplicateGroups();
        groups.Count.ShouldBe(1);
        groups[0].DurationRoundedSeconds.ShouldBe(60);
    }

    // ── D2: dismissals ───────────────────────────────────────────────────────

    [Fact]
    public void Dismiss_pair_removes_group_of_two()
    {
        var (lib, maint, temp) = Setup();
        using var _ = temp;

        var src = lib.UpsertSource(@"C:\V", "V");
        var sec = lib.UpsertSection(src, "C");
        var s1  = lib.UpsertSeries(sec, "A", false);
        var s2  = lib.UpsertSeries(sec, "B", false);

        var id1 = AddVideo(lib, s1, @"C:\V\C\a.mp4", sizeBytes: 1000, duration: 60.0);
        var id2 = AddVideo(lib, s2, @"C:\V\C\b.mp4", sizeBytes: 1000, duration: 60.0);

        maint.GetDuplicateGroups().Count.ShouldBe(1);

        maint.DismissDuplicatePair(id1, id2, DateTimeOffset.UtcNow);

        maint.GetDuplicateGroups().ShouldBeEmpty();
    }

    [Fact]
    public void Dismiss_pair_order_independent()
    {
        var (lib, maint, temp) = Setup();
        using var _ = temp;

        var src = lib.UpsertSource(@"C:\V", "V");
        var sec = lib.UpsertSection(src, "C");
        var s1  = lib.UpsertSeries(sec, "A", false);
        var s2  = lib.UpsertSeries(sec, "B", false);

        var id1 = AddVideo(lib, s1, @"C:\V\C\a.mp4", sizeBytes: 1000, duration: 60.0);
        var id2 = AddVideo(lib, s2, @"C:\V\C\b.mp4", sizeBytes: 1000, duration: 60.0);

        // Dismiss reversed order
        maint.DismissDuplicatePair(id2, id1, DateTimeOffset.UtcNow);

        maint.IsDuplicatePairDismissed(id1, id2).ShouldBeTrue();
        maint.IsDuplicatePairDismissed(id2, id1).ShouldBeTrue();
        maint.GetDuplicateGroups().ShouldBeEmpty();
    }

    [Fact]
    public void Dismiss_one_pair_in_triplet_reduces_group_not_removes()
    {
        var (lib, maint, temp) = Setup();
        using var _ = temp;

        var src = lib.UpsertSource(@"C:\V", "V");
        var sec = lib.UpsertSection(src, "C");
        var s1  = lib.UpsertSeries(sec, "A", false);
        var s2  = lib.UpsertSeries(sec, "B", false);
        var s3  = lib.UpsertSeries(sec, "C", false);

        var id1 = AddVideo(lib, s1, @"C:\V\C\a.mp4", sizeBytes: 500, duration: 30.0);
        var id2 = AddVideo(lib, s2, @"C:\V\C\b.mp4", sizeBytes: 500, duration: 30.0);
        var id3 = AddVideo(lib, s3, @"C:\V\C\c.mp4", sizeBytes: 500, duration: 30.0);

        // Dismiss only id1 vs id2; id3 is still a candidate with id2
        maint.DismissDuplicatePair(id1, id2, DateTimeOffset.UtcNow);

        var groups = maint.GetDuplicateGroups();
        // id1 is dismissed against id2 but NOT against id3 → id1 stays
        // id2 is dismissed against id1 but NOT against id3 → id2 stays
        // id3 is not dismissed against anyone → stays
        groups.Count.ShouldBe(1);
        groups[0].Videos.Count.ShouldBe(3);
    }

    [Fact]
    public void Dismiss_all_pairs_in_triplet_removes_group()
    {
        var (lib, maint, temp) = Setup();
        using var _ = temp;

        var src = lib.UpsertSource(@"C:\V", "V");
        var sec = lib.UpsertSection(src, "C");
        var s1  = lib.UpsertSeries(sec, "A", false);
        var s2  = lib.UpsertSeries(sec, "B", false);
        var s3  = lib.UpsertSeries(sec, "C2", false);

        var id1 = AddVideo(lib, s1, @"C:\V\C\a.mp4", sizeBytes: 500, duration: 30.0);
        var id2 = AddVideo(lib, s2, @"C:\V\C\b.mp4", sizeBytes: 500, duration: 30.0);
        var id3 = AddVideo(lib, s3, @"C:\V\C\c.mp4", sizeBytes: 500, duration: 30.0);

        var now = DateTimeOffset.UtcNow;
        maint.DismissDuplicatePair(id1, id2, now);
        maint.DismissDuplicatePair(id1, id3, now);
        maint.DismissDuplicatePair(id2, id3, now);

        maint.GetDuplicateGroups().ShouldBeEmpty();
    }

    [Fact]
    public void GetDismissedPairs_returns_stored_pairs()
    {
        var (lib, maint, temp) = Setup();
        using var _ = temp;

        var src = lib.UpsertSource(@"C:\V", "V");
        var sec = lib.UpsertSection(src, "C");
        var s1  = lib.UpsertSeries(sec, "A", false);
        var s2  = lib.UpsertSeries(sec, "B", false);

        var id1 = AddVideo(lib, s1, @"C:\V\C\a.mp4", sizeBytes: 1000, duration: 60.0);
        var id2 = AddVideo(lib, s2, @"C:\V\C\b.mp4", sizeBytes: 1000, duration: 60.0);

        maint.DismissDuplicatePair(id2, id1, DateTimeOffset.UtcNow);

        var pairs = maint.GetDismissedPairs();
        pairs.Count.ShouldBe(1);
        // Stored as ordered (min, max)
        pairs[0].A.ShouldBe(Math.Min(id1, id2));
        pairs[0].B.ShouldBe(Math.Max(id1, id2));
    }

    // ── BuildGroups unit tests (pure logic, no DB) ────────────────────────────

    [Fact]
    public void BuildGroups_empty_input_returns_empty()
    {
        var result = MaintenanceRepository.BuildGroups([], []);
        result.ShouldBeEmpty();
    }

    [Fact]
    public void BuildGroups_dismissed_pair_with_no_others_removes_group()
    {
        var v1 = MakeDupVideo(1, 100, 60.0);
        var v2 = MakeDupVideo(2, 100, 60.0);

        var result = MaintenanceRepository.BuildGroups([v1, v2], [(1L, 2L)]);
        result.ShouldBeEmpty();
    }

    [Fact]
    public void BuildGroups_partial_dismiss_in_triplet_keeps_all_three()
    {
        // id1 dismissed vs id2, but id3 is not dismissed against anyone
        var v1 = MakeDupVideo(1, 100, 60.0);
        var v2 = MakeDupVideo(2, 100, 60.0);
        var v3 = MakeDupVideo(3, 100, 60.0);

        var result = MaintenanceRepository.BuildGroups([v1, v2, v3], [(1L, 2L)]);
        result.Count.ShouldBe(1);
        result[0].Videos.Count.ShouldBe(3);
    }

    [Fact]
    public void BuildGroups_fully_dismissed_triplet_empty()
    {
        var v1 = MakeDupVideo(1, 100, 60.0);
        var v2 = MakeDupVideo(2, 100, 60.0);
        var v3 = MakeDupVideo(3, 100, 60.0);

        var result = MaintenanceRepository.BuildGroups(
            [v1, v2, v3],
            [(1L, 2L), (1L, 3L), (2L, 3L)]);
        result.ShouldBeEmpty();
    }

    private static DuplicateVideo MakeDupVideo(long id, long size, double duration)
        => new(id, 1L, "Creator", "Series", $@"C:\f{id}.mp4", size, duration, null, null);
}
