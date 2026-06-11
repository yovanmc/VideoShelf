using System;
using System.IO;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.Tests.TestSupport;

/// <summary>A migrated VideoShelfDb backed by a temp file, deleted on Dispose.</summary>
public sealed class AppTempDb : IDisposable
{
    public string DbPath { get; }
    public VideoShelfDb Db { get; }

    public AppTempDb()
    {
        DbPath = Path.Combine(Path.GetTempPath(), "vshelf_app_db_" + Guid.NewGuid().ToString("N") + ".db");
        Db = new VideoShelfDb(DbPath);
        Db.Migrate();
    }

    public void Dispose()
    {
        Db.Dispose();
        try { File.Delete(DbPath); } catch { }
        try { File.Delete(DbPath + "-wal"); } catch { }
        try { File.Delete(DbPath + "-shm"); } catch { }
    }
}
