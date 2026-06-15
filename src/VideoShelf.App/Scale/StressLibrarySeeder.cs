namespace VideoShelf.App.Scale;

using VideoShelf.Core.Storage;

/// <summary>Writes a StressLibrarySpec straight into the DB for render/DB-scale benchmarking
/// (no files on disk → these videos read as "missing" for playback, which is fine: we only
/// exercise browse/grid/query/thumbnail-placeholder paths). Idempotent via path-keyed upsert.</summary>
public sealed class StressLibrarySeeder(LibraryRepository repo)
{
    // No outer transaction wraps the loop below: each Upsert* method opens its own
    // connection and auto-commits, so a partial or interrupted seed is NOT rolled back.
    // This is safe because: (a) the bench always seeds into a fresh --data-dir, and
    // (b) re-seeding is fully idempotent via path-keyed upserts — any rows written by
    // a prior partial run will simply be overwritten on the next call.
    public void Seed(StressLibrarySpec spec, string sourceRoot)
    {
        // Use the same source/section/series/video write path the real scan uses so the
        // read-models (GetSectionSummaries etc.) see identical row shapes.
        // UpsertSeries takes `isStandalone` (not `sortKey`); stress series are multi-episode.
        // UpsertVideo requires a `format` param — use ".mp4" as a synthetic format.
        var sourceId = repo.UpsertSource(sourceRoot, "Stress");
        foreach (var creator in spec.Creators)
        {
            var sectionId = repo.UpsertSection(sourceId, creator.Name);
            foreach (var s in creator.Series)
            {
                var seriesId = repo.UpsertSeries(sectionId, s.BaseTitle, isStandalone: false);
                foreach (var ep in s.Episodes)
                {
                    var fullPath = System.IO.Path.Combine(sourceRoot, creator.Name, ep.RelativePath);
                    repo.UpsertVideo(seriesId, fullPath, episodeNo: ep.EpisodeNo, format: ".mp4");
                }
            }
        }
    }
}
