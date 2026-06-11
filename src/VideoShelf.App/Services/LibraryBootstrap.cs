using System.IO;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.Services;

/// <summary>Ensures the library directory exists, then opens + migrates the SQLite database.</summary>
public sealed class LibraryBootstrap(AppPaths paths)
{
    public VideoShelfDb OpenLibrary()
    {
        Directory.CreateDirectory(paths.Root);
        var db = new VideoShelfDb(paths.DatabasePath);
        db.Migrate(); // idempotent
        return db;
    }
}
