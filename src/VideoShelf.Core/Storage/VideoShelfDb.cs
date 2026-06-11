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
        using var cmd = conn.CreateCommand();
        cmd.CommandText = Schema;
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
            watched INTEGER NOT NULL DEFAULT 0
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
        """;
}
