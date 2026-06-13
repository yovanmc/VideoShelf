using System;
using System.Collections.Generic;
using VideoShelf.Core.Models;

namespace VideoShelf.Core.Storage;

public sealed record Playlist(long Id, string Name, string CreatedAt, int ItemCount);

/// <summary>CRUD for manual playlists and their ordered video items.</summary>
public sealed class PlaylistRepository(VideoShelfDb db)
{
    // ── Playlist-level ────────────────────────────────────────────────────────

    public long Create(string name, DateTimeOffset now)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO playlists(name, created_at, sort_order)
            VALUES($name, $at, COALESCE((SELECT MAX(sort_order)+1 FROM playlists), 0));
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$at", now.ToString("o"));
        return (long)cmd.ExecuteScalar()!;
    }

    public void Rename(long id, string name)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE playlists SET name=$name WHERE id=$id";
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void Delete(long id)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM playlists WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<Playlist> GetAll()
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT p.id, p.name, p.created_at, p.sort_order,
                   COUNT(pi.video_id) AS item_count
            FROM playlists p
            LEFT JOIN playlist_items pi ON pi.playlist_id = p.id
            GROUP BY p.id, p.name, p.created_at, p.sort_order
            ORDER BY p.sort_order, p.id
            """;
        var list = new List<Playlist>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new Playlist(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetInt32(4)));
        return list;
    }

    // ── Item-level ────────────────────────────────────────────────────────────

    public void AddItem(long playlistId, long videoId)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        // position = max(position)+1 for the playlist; INSERT OR IGNORE handles dup PK
        cmd.CommandText = """
            INSERT OR IGNORE INTO playlist_items(playlist_id, video_id, position)
            VALUES($pid, $vid,
                   COALESCE((SELECT MAX(position)+1 FROM playlist_items WHERE playlist_id=$pid), 0));
            """;
        cmd.Parameters.AddWithValue("$pid", playlistId);
        cmd.Parameters.AddWithValue("$vid", videoId);
        cmd.ExecuteNonQuery();
    }

    public void RemoveItem(long playlistId, long videoId)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM playlist_items WHERE playlist_id=$pid AND video_id=$vid";
        cmd.Parameters.AddWithValue("$pid", playlistId);
        cmd.Parameters.AddWithValue("$vid", videoId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Moves <paramref name="videoId"/> to <paramref name="newPosition"/> (0-based) within the playlist,
    /// renumbering all items in a single transaction.
    /// </summary>
    public void Move(long playlistId, long videoId, int newPosition)
    {
        using var conn = db.Open();
        using var tx = conn.BeginTransaction();

        // 1. Load current ordered video ids
        List<long> ids;
        using (var sel = conn.CreateCommand())
        {
            sel.CommandText = "SELECT video_id FROM playlist_items WHERE playlist_id=$pid ORDER BY position";
            sel.Parameters.AddWithValue("$pid", playlistId);
            ids = new List<long>();
            using var r = sel.ExecuteReader();
            while (r.Read()) ids.Add(r.GetInt64(0));
        }

        // 2. Move the target
        if (!ids.Remove(videoId)) { tx.Commit(); return; }
        var clamped = Math.Max(0, Math.Min(ids.Count, newPosition));
        ids.Insert(clamped, videoId);

        // 3. Rewrite positions 0..n-1
        using (var upd = conn.CreateCommand())
        {
            upd.CommandText = "UPDATE playlist_items SET position=$pos WHERE playlist_id=$pid AND video_id=$vid";
            var pPos = upd.Parameters.Add("$pos", Microsoft.Data.Sqlite.SqliteType.Integer);
            var pPid = upd.Parameters.Add("$pid", Microsoft.Data.Sqlite.SqliteType.Integer);
            var pVid = upd.Parameters.Add("$vid", Microsoft.Data.Sqlite.SqliteType.Integer);
            pPid.Value = playlistId;
            for (int i = 0; i < ids.Count; i++)
            {
                pPos.Value = i;
                pVid.Value = ids[i];
                upd.ExecuteNonQuery();
            }
        }

        tx.Commit();
    }

    /// <summary>
    /// Returns EpisodeView items for the playlist in position order,
    /// excluding missing videos — mirroring LibraryRepository.GetEpisodes projection.
    /// </summary>
    public IReadOnlyList<EpisodeView> GetItems(long playlistId)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT v.id, v.series_id, v.file_path, v.episode_no, se.base_title, v.watched, v.missing
            FROM playlist_items pi
            JOIN videos v ON v.id = pi.video_id
            JOIN series se ON se.id = v.series_id
            WHERE pi.playlist_id = $pid AND v.missing = 0
            ORDER BY pi.position
            """;
        cmd.Parameters.AddWithValue("$pid", playlistId);
        var list = new List<EpisodeView>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var episodeNo = r.GetInt32(3);
            var baseTitle = r.GetString(4);
            var title = episodeNo <= 1 ? baseTitle : $"{baseTitle} {episodeNo}";
            list.Add(new EpisodeView(
                r.GetInt64(0), r.GetInt64(1), r.GetString(2), episodeNo, title,
                r.GetInt64(5) != 0, r.GetInt64(6) != 0));
        }
        return list;
    }
}
