using System;
using System.IO;
using Shouldly;
using VideoShelf.App.Services;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.Tests;

public class LibraryBootstrapTests
{
    [Fact]
    public void OpenLibrary_creates_and_migrates_db_at_given_path()
    {
        var dir = Path.Combine(Path.GetTempPath(), "vshelf_app_" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(dir);
            var bootstrap = new LibraryBootstrap(paths);

            VideoShelfDb db = bootstrap.OpenLibrary();

            File.Exists(paths.DatabasePath).ShouldBeTrue();
            // A migrated DB can round-trip a source without throwing.
            var repo = new LibraryRepository(db);
            repo.UpsertSource(@"C:\Vids", "Vids");
            repo.GetSources().Count.ShouldBe(1);
            db.Dispose();
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void AppPaths_resolves_db_and_thumbs_under_root()
    {
        var paths = new AppPaths(@"C:\Root\VideoShelf");

        paths.DatabasePath.ShouldBe(@"C:\Root\VideoShelf\library.db");
        paths.ThumbnailDirectory.ShouldBe(@"C:\Root\VideoShelf\thumbs");
    }
}
