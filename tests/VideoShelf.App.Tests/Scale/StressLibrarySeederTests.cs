using VideoShelf.App.Scale;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.Tests.Scale;

public class StressLibrarySeederTests
{
    [Fact]
    public void Seeder_writes_all_rows_and_is_idempotent()
    {
        using var db = new AppTempDb();
        var repo = new LibraryRepository(db.Db);
        var spec = StressLibrarySpec.Generate(20, 10, 200, seed: 5);

        var seeder = new StressLibrarySeeder(repo);
        seeder.Seed(spec, sourceRoot: @"C:\stress");

        Assert.Equal(20, repo.GetSectionSummaries().Count);
        var biggest = repo.GetSectionSummaries().OrderByDescending(s => s.VideoCount).First();
        Assert.True(biggest.VideoCount > 0);

        // Idempotent: re-seeding the same spec does not duplicate rows.
        seeder.Seed(spec, sourceRoot: @"C:\stress");
        Assert.Equal(20, repo.GetSectionSummaries().Count);
    }
}
