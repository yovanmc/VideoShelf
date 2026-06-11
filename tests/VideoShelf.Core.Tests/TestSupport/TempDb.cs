using System;
using System.IO;
using VideoShelf.Core.Storage;

namespace VideoShelf.Core.Tests.TestSupport;

/// <summary>A VideoShelfDb backed by a temp .db file, migrated and deleted on Dispose.</summary>
public sealed class TempDb : IDisposable
{
    public string DbPath { get; }
    public VideoShelfDb Db { get; }

    public TempDb()
    {
        DbPath = Path.Combine(Path.GetTempPath(), "vshelf_db_" + Guid.NewGuid().ToString("N") + ".db");
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
