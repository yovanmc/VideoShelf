using System.Threading.Tasks;
using Shouldly;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;
using Xunit;

namespace VideoShelf.App.Tests;

public class FavoritesViewModelTests
{
    private static (CurationRepository curation, LibraryRepository lib, long videoId) Seed(AppTempDb temp)
    {
        var lib = new LibraryRepository(temp.Db);
        var ser = lib.UpsertSeries(lib.UpsertSection(lib.UpsertSource(@"C:\V", "V"), "S"), "Base", false);
        var vid = lib.UpsertVideo(ser, @"C:\V\S\a.mp4", 1, ".mp4");
        var curation = new CurationRepository(temp.Db);
        return (curation, lib, vid);
    }

    [Fact]
    public async Task LoadAsync_populates_Favorites_when_a_video_is_marked_favorite()
    {
        using var temp = new AppTempDb();
        var (curation, lib, videoId) = Seed(temp);
        curation.SetFavorite(videoId, true);
        var vm = new FavoritesViewModel(curation, lib);

        await vm.LoadAsync();

        vm.Favorites.Count.ShouldBe(1);
        vm.Favorites[0].VideoId.ShouldBe(videoId);
        vm.HasFavorites.ShouldBeTrue();
    }

    [Fact]
    public async Task LoadAsync_produces_empty_collection_when_no_favorites()
    {
        using var temp = new AppTempDb();
        var (curation, lib, _) = Seed(temp);
        var vm = new FavoritesViewModel(curation, lib);

        await vm.LoadAsync();

        vm.Favorites.ShouldBeEmpty();
        vm.HasFavorites.ShouldBeFalse();
    }

    [Fact]
    public async Task Card_Play_raises_PlayRequested_with_resolved_episode()
    {
        using var temp = new AppTempDb();
        var (curation, lib, videoId) = Seed(temp);
        curation.SetFavorite(videoId, true);
        var vm = new FavoritesViewModel(curation, lib);
        await vm.LoadAsync();

        EpisodeView? played = null;
        vm.PlayRequested += (_, ep) => played = ep;
        vm.Favorites[0].PlayCommand.Execute(null);

        played.ShouldNotBeNull();
        played!.VideoId.ShouldBe(videoId);
    }

    // ── M21 Group C skeleton-loader regression ────────────────────────────────

    /// <summary>IsLoading must be false after LoadAsync completes (finally-clause guard).</summary>
    [Fact]
    public async Task IsLoading_is_false_after_LoadAsync_completes()
    {
        using var temp = new AppTempDb();
        var (curation, lib, videoId) = Seed(temp);
        curation.SetFavorite(videoId, true);
        var vm = new FavoritesViewModel(curation, lib);

        vm.IsLoading.ShouldBeFalse(); // starts false

        await vm.LoadAsync();

        vm.IsLoading.ShouldBeFalse(); // cleared in finally
    }
}
