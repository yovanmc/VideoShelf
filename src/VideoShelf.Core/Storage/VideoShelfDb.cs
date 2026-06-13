using System;
using Microsoft.Data.Sqlite;

namespace VideoShelf.Core.Storage;

/// <summary>Owns the SQLite connection string and schema. Open() returns a ready connection; Migrate() is idempotent.</summary>
public sealed class VideoShelfDb : IDisposable
{
    private readonly string _connectionString;

    public VideoShelfDb(string dbPath)
        => _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ToString();

    public SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
        pragma.ExecuteNonQuery();
        return conn;
    }

    public void Migrate()
    {
        using var conn = Open();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = Schema;
            cmd.ExecuteNonQuery();
        }

        // Idempotent, crash-safe additions for databases created by an earlier schema.
        // ALTER TABLE ADD COLUMN has no IF NOT EXISTS in SQLite, so guard on table_info.
        EnsureColumn(conn, "videos", "missing", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(conn, "videos", "added_at", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(conn, "videos", "resume_position", "REAL");
        // resume_updated_at: ISO8601 timestamp of the last resume write (Milestone 4 discovery ordering)
        EnsureColumn(conn, "videos", "resume_updated_at", "TEXT");
        // duration: pre-schema DBs may lack this column even though it is in base CREATE TABLE
        EnsureColumn(conn, "videos", "duration", "REAL");
        // M16-C: favorites + star ratings
        EnsureColumn(conn, "videos", "is_favorite", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(conn, "videos", "rating", "INTEGER NOT NULL DEFAULT 0");
        // M16-E: watchlist / watch-later
        EnsureColumn(conn, "videos", "in_watchlist", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(conn, "videos", "watchlist_at", "TEXT");
        CreateAddedAtIndex(conn);
    }

    private static void EnsureColumn(SqliteConnection conn, string table, string column, string definition)
    {
        bool exists;
        using (var check = conn.CreateCommand())
        {
            check.CommandText = $"SELECT COUNT(*) FROM pragma_table_info($t) WHERE name = $c";
            check.Parameters.AddWithValue("$t", table);
            check.Parameters.AddWithValue("$c", column);
            exists = (long)check.ExecuteScalar()! > 0;
        }
        if (exists) return;

        using var alter = conn.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition}";
        alter.ExecuteNonQuery();
    }

    private static void CreateAddedAtIndex(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE INDEX IF NOT EXISTS ix_videos_added_at ON videos(added_at)";
        cmd.ExecuteNonQuery();
    }

    public void Dispose() => SqliteConnection.ClearAllPools();

    private const string Schema = """
        CREATE TABLE IF NOT EXISTS sources (
            id INTEGER PRIMARY KEY,
            root_path TEXT NOT NULL UNIQUE,
            display_name TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS sections (
            id INTEGER PRIMARY KEY,
            source_id INTEGER NOT NULL REFERENCES sources(id) ON DELETE CASCADE,
            folder_name TEXT NOT NULL,
            display_name TEXT NOT NULL,
            UNIQUE(source_id, folder_name)
        );
        CREATE TABLE IF NOT EXISTS series (
            id INTEGER PRIMARY KEY,
            section_id INTEGER NOT NULL REFERENCES sections(id) ON DELETE CASCADE,
            base_title TEXT NOT NULL,
            sort_key TEXT NOT NULL,
            is_standalone INTEGER NOT NULL DEFAULT 0,
            UNIQUE(section_id, base_title)
        );
        CREATE TABLE IF NOT EXISTS videos (
            id INTEGER PRIMARY KEY,
            series_id INTEGER NOT NULL REFERENCES series(id) ON DELETE CASCADE,
            file_path TEXT NOT NULL UNIQUE,
            episode_no INTEGER NOT NULL,
            raw_filename TEXT NOT NULL,
            format TEXT NOT NULL,
            duration REAL,
            thumbnail_path TEXT,
            watched INTEGER NOT NULL DEFAULT 0,
            missing INTEGER NOT NULL DEFAULT 0,
            added_at TEXT NOT NULL DEFAULT '',
            resume_position REAL
        );
        CREATE TABLE IF NOT EXISTS section_tags (
            section_id INTEGER NOT NULL REFERENCES sections(id) ON DELETE CASCADE,
            tag TEXT NOT NULL,
            PRIMARY KEY(section_id, tag)
        );
        CREATE TABLE IF NOT EXISTS watch_events (
            id INTEGER PRIMARY KEY,
            video_id INTEGER NOT NULL REFERENCES videos(id) ON DELETE CASCADE,
            watched_at TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS grouping_overrides (
            id INTEGER PRIMARY KEY,
            section_id INTEGER NOT NULL REFERENCES sections(id) ON DELETE CASCADE,
            file_path TEXT NOT NULL,
            override_base_title TEXT,
            override_episode_no INTEGER,
            UNIQUE(section_id, file_path)
        );
        CREATE TABLE IF NOT EXISTS settings (
            key TEXT PRIMARY KEY,
            value TEXT
        );
        CREATE INDEX IF NOT EXISTS ix_videos_series ON videos(series_id);
        CREATE INDEX IF NOT EXISTS ix_sections_source ON sections(source_id);
        CREATE INDEX IF NOT EXISTS ix_series_section ON series(section_id);
        CREATE TABLE IF NOT EXISTS creator_art (
            section_id INTEGER NOT NULL PRIMARY KEY REFERENCES sections(id) ON DELETE CASCADE,
            image_path TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS video_chapters (
            video_id      INTEGER NOT NULL,
            idx           INTEGER NOT NULL,
            name          TEXT    NOT NULL DEFAULT '',
            start_seconds REAL    NOT NULL DEFAULT 0,
            PRIMARY KEY (video_id, idx),
            FOREIGN KEY (video_id) REFERENCES videos(id) ON DELETE CASCADE
        );
        CREATE TABLE IF NOT EXISTS series_tags (
            series_id INTEGER NOT NULL REFERENCES series(id) ON DELETE CASCADE,
            tag TEXT NOT NULL,
            PRIMARY KEY(series_id, tag)
        );
        CREATE TABLE IF NOT EXISTS video_tags (
            video_id INTEGER NOT NULL REFERENCES videos(id) ON DELETE CASCADE,
            tag TEXT NOT NULL,
            PRIMARY KEY(video_id, tag)
        );
        CREATE INDEX IF NOT EXISTS ix_series_tags_tag ON series_tags(tag);
        CREATE INDEX IF NOT EXISTS ix_video_tags_tag ON video_tags(tag);
        CREATE TABLE IF NOT EXISTS smart_views (
            id INTEGER PRIMARY KEY,
            name TEXT NOT NULL,
            definition TEXT NOT NULL,
            sort_order INTEGER NOT NULL DEFAULT 0,
            show_on_home INTEGER NOT NULL DEFAULT 1,
            created_at TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS playlists (
            id INTEGER PRIMARY KEY, name TEXT NOT NULL,
            created_at TEXT NOT NULL, sort_order INTEGER NOT NULL DEFAULT 0
        );
        CREATE TABLE IF NOT EXISTS playlist_items (
            playlist_id INTEGER NOT NULL REFERENCES playlists(id) ON DELETE CASCADE,
            video_id INTEGER NOT NULL REFERENCES videos(id) ON DELETE CASCADE,
            position INTEGER NOT NULL, PRIMARY KEY(playlist_id, video_id)
        );
        CREATE TABLE IF NOT EXISTS video_art (
            video_id  INTEGER NOT NULL PRIMARY KEY REFERENCES videos(id)  ON DELETE CASCADE,
            image_path TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS series_art (
            series_id INTEGER NOT NULL PRIMARY KEY REFERENCES series(id)  ON DELETE CASCADE,
            image_path TEXT NOT NULL
        );
        """;
}
