namespace VideoShelf.App.Scale;

using VideoShelf.Core.Storage;

/// <summary>Writes a StressLibrarySpec straight into the DB for render/DB-scale benchmarking
/// (no files on disk → these videos read as "missing" for playback, which is fine: we only
/// exercise browse/grid/query/thumbnail-placeholder paths). Idempotent via path-keyed upsert.</summary>
public sealed class StressLibrarySeeder(LibraryRepository repo)
{
    public void Seed(StressLibrarySpec spec, string sourceRoot)
    {
        // Use the same source/section/series/video write path the real scan uses so the
        // read-models (GetSectionSummaries etc.) see identical row shapes.
        // Note: RunInTransaction opens its own connection for the tx boundary; the individual
        // upsert methods each open their own connections (per-operation pattern). This means
        // the transaction boundary is advisory here — each upsert auto-commits internally.
        // For bulk speed we keep the outer tx and route the calls through it.
        // Adaptation: LibraryRepository.UpsertSeries takes `isStandalone` (not `sortKey`).
        // Seeder uses isStandalone=false (all stress series are multi-episode).
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
