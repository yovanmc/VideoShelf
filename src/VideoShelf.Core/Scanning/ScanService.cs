using System;
using System.IO;
using System.Linq;
using VideoShelf.Core.Naming;
using VideoShelf.Core.Storage;

namespace VideoShelf.Core.Scanning;

/// <summary>
/// Orchestrates a full source scan: discover sections/files, group into series/standalones,
/// and upsert into the library. Idempotent — re-scanning the same source updates in place
/// (upserts keyed by natural keys), so watched-state and IDs survive. Videos no longer found
/// on disk are marked missing (never deleted from the index); found videos clear the flag.
/// Returns a <see cref="ScanResult"/> diff describing what changed.
/// </summary>
public sealed class ScanService(VideoShelfDb db, LibraryRepository library)
{
    /// <summary>
    /// Scans <paramref name="sourceRoot"/>, upserts everything found, and returns a diff.
    /// Callers that previously ignored the void return can safely ignore the <see cref="ScanResult"/>.
    /// </summary>
    public ScanResult ScanSource(string sourceRoot, string displayName)
    {
        var sourceId = library.UpsertSource(sourceRoot, displayName);

        // Snapshot BEFORE: file_path -> wasMissing, for this source.
        // Must be taken BEFORE MarkAllMissingForSource so we see the real prior state.
        var before = library.GetVideoPathStates(sourceId);

        // Tentatively mark everything under this source missing; clear each file we re-find.
        library.MarkAllMissingForSource(sourceId);

        int added = 0, restored = 0, updated = 0;
        foreach (var section in FolderScanner.Scan(sourceRoot))
        {
            var sectionId = library.UpsertSection(sourceId, section.FolderName);
            // M18-B: load per-section grouping overrides (keyed by bare file name) and pass
            // them to the new overload so split/merge/manual-order survive every rescan.
            var overrides = library.GetGroupingOverrides(sectionId);
            var grouped = SectionGrouper.Group(section.Files.Select(f => f.FileName).ToList(), overrides);

            foreach (var series in grouped.Series)
            {
                var seriesId = library.UpsertSeries(sectionId, series.BaseTitle, series.IsStandalone);
                foreach (var episode in series.Episodes)
                {
                    var full = Path.Combine(sourceRoot, section.FolderName, episode.FileName);
                    long? size = TryFileSize(full);
                    library.UpsertVideo(seriesId, full, episode.EpisodeNumber, Path.GetExtension(episode.FileName), size);
                    library.ClearMissing(full);

                    if (!before.TryGetValue(full, out var wasMissing))
                        added++;
                    else if (wasMissing)
                        restored++;
                    else
                        updated++;
                }
            }
        }

        int missing = library.CountMissingForSource(sourceId);
        library.SetSourceLastScanUtc(sourceId, DateTimeOffset.UtcNow);
        return new ScanResult(added, updated, restored, missing);
    }

    /// <summary>Returns the file size in bytes, or null if the file cannot be stat'd.</summary>
    private static long? TryFileSize(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return null; }
    }
}
