using System;
using System.Collections.Generic;
using VideoShelf.Core.Storage;

namespace VideoShelf.Core.Storage;

/// <summary>Watched/unwatched state and the watch-event history that feeds discovery.</summary>
public sealed class WatchRepository(VideoShelfDb db)
{
    public void SetWatched(long videoId, bool watched)
    {
        using var conn = db.Open();
        using var tx = conn.BeginTransaction();

        using (var upd = conn.CreateCommand())
        {
            upd.CommandText = "UPDATE videos SET watched=$w WHERE id=$id";
            upd.Parameters.AddWithValue("$w", watched ? 1 : 0);
            upd.Parameters.AddWithValue("$id", videoId);
            upd.ExecuteNonQuery();
        }

        if (watched)
        {
            using (var ins = conn.CreateCommand())
            {
                ins.CommandText = "INSERT INTO watch_events(video_id, watched_at) VALUES($id, $at)";
                ins.Parameters.AddWithValue("$id", videoId);
                ins.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("o"));
                ins.ExecuteNonQuery();
            }

            using var clr = conn.CreateCommand();
            clr.CommandText = "UPDATE videos SET resume_position = NULL, resume_updated_at = NULL WHERE id = $id";
            clr.Parameters.AddWithValue("$id", videoId);
            clr.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public void SetWatchedForSeries(long seriesId, bool watched)
    {
        using var conn = db.Open();
        using var tx = conn.BeginTransaction();

        if (watched)
        {
            // Collect affected ids first so we can insert per-video events.
            var ids = new System.Collections.Generic.List<long>();
            using (var sel = conn.CreateCommand())
            {
                sel.CommandText = "SELECT id FROM videos WHERE series_id=$s AND missing=0";
                sel.Parameters.AddWithValue("$s", seriesId);
                using var r = sel.ExecuteReader();
                while (r.Read()) ids.Add(r.GetInt64(0));
            }

            var at = DateTimeOffset.UtcNow.ToString("o");
            foreach (var vid in ids)
            {
                using var upd = conn.CreateCommand();
                upd.CommandText = "UPDATE videos SET watched=1 WHERE id=$id";
                upd.Parameters.AddWithValue("$id", vid);
                upd.ExecuteNonQuery();

                using var ins = conn.CreateCommand();
                ins.CommandText = "INSERT INTO watch_events(video_id, watched_at) VALUES($id, $at)";
                ins.Parameters.AddWithValue("$id", vid);
                ins.Parameters.AddWithValue("$at", at);
                ins.ExecuteNonQuery();

                using var clr = conn.CreateCommand();
                clr.CommandText = "UPDATE videos SET resume_position=NULL, resume_updated_at=NULL WHERE id=$id";
                clr.Parameters.AddWithValue("$id", vid);
                clr.ExecuteNonQuery();
            }
        }
        else
        {
            using var upd = conn.CreateCommand();
            upd.CommandText = "UPDATE videos SET watched=0 WHERE series_id=$s AND missing=0";
            upd.Parameters.AddWithValue("$s", seriesId);
            upd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public void SetWatchedForSection(long sectionId, bool watched)
    {
        using var conn = db.Open();
        using var tx = conn.BeginTransaction();

        if (watched)
        {
            // Collect affected ids via series join.
            var ids = new System.Collections.Generic.List<long>();
            using (var sel = conn.CreateCommand())
            {
                sel.CommandText = "SELECT v.id FROM videos v JOIN series s ON s.id=v.series_id WHERE s.section_id=$sec AND v.missing=0";
                sel.Parameters.AddWithValue("$sec", sectionId);
                using var r = sel.ExecuteReader();
                while (r.Read()) ids.Add(r.GetInt64(0));
            }

            var at = DateTimeOffset.UtcNow.ToString("o");
            foreach (var vid in ids)
            {
                using var upd = conn.CreateCommand();
                upd.CommandText = "UPDATE videos SET watched=1 WHERE id=$id";
                upd.Parameters.AddWithValue("$id", vid);
                upd.ExecuteNonQuery();

                using var ins = conn.CreateCommand();
                ins.CommandText = "INSERT INTO watch_events(video_id, watched_at) VALUES($id, $at)";
                ins.Parameters.AddWithValue("$id", vid);
                ins.Parameters.AddWithValue("$at", at);
                ins.ExecuteNonQuery();

                using var clr = conn.CreateCommand();
                clr.CommandText = "UPDATE videos SET resume_position=NULL, resume_updated_at=NULL WHERE id=$id";
                clr.Parameters.AddWithValue("$id", vid);
                clr.ExecuteNonQuery();
            }
        }
        else
        {
            using var upd = conn.CreateCommand();
            upd.CommandText = "UPDATE videos SET watched=0 WHERE id IN (SELECT v.id FROM videos v JOIN series s ON s.id=v.series_id WHERE s.section_id=$sec AND v.missing=0)";
            upd.Parameters.AddWithValue("$sec", sectionId);
            upd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public bool IsWatched(long videoId)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT watched FROM videos WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", videoId);
        return cmd.ExecuteScalar() is long l && l != 0;
    }

    public IReadOnlyList<long> RecentlyWatchedVideoIds(int limit)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT video_id FROM watch_events ORDER BY watched_at DESC LIMIT $n";
        cmd.Parameters.AddWithValue("$n", limit);
        var list = new List<long>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(r.GetInt64(0));
        return list;
    }
}
