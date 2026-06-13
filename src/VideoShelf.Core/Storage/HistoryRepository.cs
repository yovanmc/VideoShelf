using System.Collections.Generic;

namespace VideoShelf.Core.Storage;

/// <summary>
/// A single watch-event row joined with video and series details.
/// A video may appear multiple times (once per watch event).
/// </summary>
public sealed record HistoryEntry(
    long VideoId,
    long SeriesId,
    string SeriesTitle,
    int EpisodeNo,
    bool IsStandalone,
    string WatchedAt,
    string? ThumbnailSeedPath);

/// <summary>Reads the watch-event history for the History page.</summary>
public sealed class HistoryRepository(VideoShelfDb db)
{
    public IReadOnlyList<HistoryEntry> GetHistory(int limit)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT v.id, s.id, s.base_title, v.episode_no, s.is_standalone,
                   we.watched_at, v.thumbnail_path
            FROM watch_events we
            JOIN videos v ON v.id = we.video_id
            JOIN series s ON s.id = v.series_id
            ORDER BY we.watched_at DESC
            LIMIT $n
            """;
        cmd.Parameters.AddWithValue("$n", limit);

        var list = new List<HistoryEntry>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new HistoryEntry(
                VideoId: r.GetInt64(0),
                SeriesId: r.GetInt64(1),
                SeriesTitle: r.GetString(2),
                EpisodeNo: r.GetInt32(3),
                IsStandalone: r.GetInt32(4) != 0,
                WatchedAt: r.GetString(5),
                ThumbnailSeedPath: r.IsDBNull(6) ? null : r.GetString(6)));
        }
        return list;
    }
}
