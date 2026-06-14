using System.IO;
using System.Linq;
using Shouldly;
using VideoShelf.Core.Scanning;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;

namespace VideoShelf.Core.Tests.Scanning;

/// <summary>
/// Tests for <see cref="ScanResult"/> diff tracking (M18-A3) and <c>size_bytes</c> capture (M18-A2).
/// Uses real temp files so FileInfo.Length is accurate.
/// </summary>
public class ScanDiffTests
{
    [Fact]
    public void First_scan_counts_all_files_as_Added()
    {
        using var temp = new TempDb();
        using var dir = new TempDir();
        dir.Touch("Creator A/Video One.mp4");
        dir.Touch("Creator A/Video Two.mp4");
        dir.Touch("Solo/Standalone.mkv");

        var lib = new LibraryRepository(temp.Db);
        var scan = new ScanService(temp.Db, lib);

        var result = scan.ScanSource(dir.Path, "My Videos");

        result.Added.ShouldBe(3);
        result.Updated.ShouldBe(0);
        result.Restored.ShouldBe(0);
        result.Missing.ShouldBe(0);
    }

    [Fact]
    public void Rescan_with_same_files_counts_all_as_Updated()
    {
        using var temp = new TempDb();
        using var dir = new TempDir();
        dir.Touch("Creator A/Video One.mp4");
        dir.Touch("Creator A/Video Two.mp4");

        var lib = new LibraryRepository(temp.Db);
        var scan = new ScanService(temp.Db, lib);

        scan.ScanSource(dir.Path, "My Videos"); // first scan → all Added

        var result = scan.ScanSource(dir.Path, "My Videos"); // second scan → all Updated

        result.Added.ShouldBe(0);
        result.Updated.ShouldBe(2);
        result.Restored.ShouldBe(0);
        result.Missing.ShouldBe(0);
    }

    [Fact]
    public void Add_a_file_and_delete_a_file_produces_correct_diff()
    {
        using var temp = new TempDb();
        using var dir = new TempDir();
        var fileA = dir.Touch("Creator A/Video One.mp4");
        dir.Touch("Creator A/Video Two.mp4");

        var lib = new LibraryRepository(temp.Db);
        var scan = new ScanService(temp.Db, lib);

        scan.ScanSource(dir.Path, "My Videos"); // first scan

        // Delete Video One, add Video Three.
        File.Delete(fileA);
        dir.Touch("Creator A/Video Three.mp4");

        var result = scan.ScanSource(dir.Path, "My Videos");

        result.Added.ShouldBe(1);    // Video Three is new
        result.Updated.ShouldBe(1);  // Video Two was already present
        result.Restored.ShouldBe(0);
        result.Missing.ShouldBe(1);  // Video One is gone
    }

    [Fact]
    public void Restoring_a_deleted_file_shows_as_Restored()
    {
        using var temp = new TempDb();
        using var dir = new TempDir();
        var fileA = dir.Touch("Creator A/Video One.mp4");
        dir.Touch("Creator A/Video Two.mp4");

        var lib = new LibraryRepository(temp.Db);
        var scan = new ScanService(temp.Db, lib);

        scan.ScanSource(dir.Path, "My Videos"); // first scan

        // Delete Video One then rescan → it goes missing
        File.Delete(fileA);
        scan.ScanSource(dir.Path, "My Videos");

        // Restore Video One → it should be Restored on next scan
        dir.Touch("Creator A/Video One.mp4");
        var result = scan.ScanSource(dir.Path, "My Videos");

        result.Added.ShouldBe(0);
        result.Updated.ShouldBe(1);  // Video Two continuously present
        result.Restored.ShouldBe(1); // Video One came back
        result.Missing.ShouldBe(0);
    }

    [Fact]
    public void Size_bytes_is_populated_after_scan()
    {
        using var temp = new TempDb();
        using var dir = new TempDir();
        // Write 100 bytes so size is non-zero and meaningful.
        var fullPath = dir.Touch("Creator A/Video One.mp4");
        System.IO.File.WriteAllBytes(fullPath, new byte[100]);

        var lib = new LibraryRepository(temp.Db);
        var scan = new ScanService(temp.Db, lib);
        scan.ScanSource(dir.Path, "My Videos");

        var sourceId = lib.GetSources().Single().Id;
        var section = lib.GetSections(sourceId).Single();
        var series = lib.GetSeriesForSection(section.Id).Single();
        var video = lib.GetVideosForSeries(series.Id).Single();

        video.SizeBytes.ShouldBe(100L);
    }

    [Fact]
    public void GetVideosNeedingSize_returns_only_rows_with_null_size_bytes()
    {
        using var temp = new TempDb();
        using var dir = new TempDir();
        // Use episode-numbered names so they group into one series.
        dir.Touch("Creator A/Cool Story 1.mp4");
        dir.Touch("Creator A/Cool Story 2.mp4");

        var lib = new LibraryRepository(temp.Db);
        var scan = new ScanService(temp.Db, lib);
        scan.ScanSource(dir.Path, "My Videos");

        var sourceId = lib.GetSources().Single().Id;
        var section = lib.GetSections(sourceId).Single();
        var series = lib.GetSeriesForSection(section.Id).Single();
        var videos = lib.GetVideosForSeries(series.Id);
        videos.Count.ShouldBe(2);

        // Manually set size to null for video[0] to simulate a legacy row without size_bytes.
        using var conn = temp.Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE videos SET size_bytes = NULL WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", videos[0].Id);
        cmd.ExecuteNonQuery();

        var needing = lib.GetVideosNeedingSize();
        needing.Count.ShouldBe(1);
        needing[0].Id.ShouldBe(videos[0].Id);
    }

    [Fact]
    public void SetSizeBytes_writes_and_GetVideosNeedingSize_clears_it()
    {
        using var temp = new TempDb();
        using var dir = new TempDir();
        dir.Touch("Creator A/Video One.mp4");

        var lib = new LibraryRepository(temp.Db);
        var scan = new ScanService(temp.Db, lib);
        scan.ScanSource(dir.Path, "My Videos");

        var sourceId = lib.GetSources().Single().Id;
        var section = lib.GetSections(sourceId).Single();
        var series = lib.GetSeriesForSection(section.Id).Single();
        var video = lib.GetVideosForSeries(series.Id).Single();

        // Clear size_bytes to simulate a legacy row.
        using (var conn = temp.Db.Open())
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE videos SET size_bytes = NULL WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", video.Id);
            cmd.ExecuteNonQuery();
        }

        lib.GetVideosNeedingSize().Count.ShouldBe(1);

        lib.SetSizeBytes(video.Id, 42L);

        lib.GetVideosNeedingSize().Count.ShouldBe(0);
    }

    [Fact]
    public void SetSourceLastScanUtc_and_GetSourceLastScanUtc_round_trip()
    {
        using var temp = new TempDb();
        using var dir = new TempDir();
        dir.Touch("Creator A/Video One.mp4");

        var lib = new LibraryRepository(temp.Db);
        var scan = new ScanService(temp.Db, lib);
        scan.ScanSource(dir.Path, "My Videos");

        var sourceId = lib.GetSources().Single().Id;

        // ScanSource already wrote last_scan_utc; just verify it is non-null.
        var ts = lib.GetSourceLastScanUtc(sourceId);
        ts.ShouldNotBeNull();

        // Write a specific timestamp and round-trip it.
        var expected = new System.DateTimeOffset(2026, 1, 15, 12, 0, 0, System.TimeSpan.Zero);
        lib.SetSourceLastScanUtc(sourceId, expected);
        var actual = lib.GetSourceLastScanUtc(sourceId);
        actual.ShouldNotBeNull();
        actual!.Value.UtcDateTime.ShouldBe(expected.UtcDateTime);
    }
}
