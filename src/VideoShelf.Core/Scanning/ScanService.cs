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
/// </summary>
public sealed class ScanService(VideoShelfDb db, LibraryRepository library)
{
    public void ScanSource(string sourceRoot, string displayName)
    {
        var sourceId = library.UpsertSource(sourceRoot, displayName);

        // Tentatively mark everything under this source missing; clear each file we re-find.
        library.MarkAllMissingForSource(sourceId);

        foreach (var section in FolderScanner.Scan(sourceRoot))
        {
            var sectionId = library.UpsertSection(sourceId, section.FolderName);
            var grouped = SectionGrouper.Group(section.Files.Select(f => f.FileName).ToList());

            foreach (var series in grouped.Series)
            {
                var seriesId = library.UpsertSeries(sectionId, series.BaseTitle, series.IsStandalone);
                foreach (var episode in series.Episodes)
                {
                    var full = Path.Combine(sourceRoot, section.FolderName, episode.FileName);
                    library.UpsertVideo(seriesId, full, episode.EpisodeNumber, Path.GetExtension(episode.FileName));
                    library.ClearMissing(full);
                }
            }
        }
    }
}
