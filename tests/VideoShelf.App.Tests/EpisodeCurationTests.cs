using Shouldly;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;
using Xunit;

namespace VideoShelf.App.Tests;

public class EpisodeCurationTests
{
    private static (EpisodeViewModel vm, CurationRepository curation) Make(AppTempDb temp)
    {
        var lib = new LibraryRepository(temp.Db);
        var ser = lib.UpsertSeries(lib.UpsertSection(lib.UpsertSource(@"C:\V", "V"), "S"), "Base", false);
        var videoId = lib.UpsertVideo(ser, @"C:\V\S\a.mp4", 1, ".mp4");
        var watch = new WatchRepository(temp.Db);
        var curation = new CurationRepository(temp.Db);
        var view = new EpisodeView(videoId, 1, @"C:\V\S\a.mp4", 1, "Base", Watched: false, Missing: false);
        var vm = new EpisodeViewModel(view, watch, tags: null, curation: curation);
        return (vm, curation);
    }

    [Fact]
    public void ToggleFavorite_flips_flag_and_persists()
    {
        using var temp = new AppTempDb();
        var (vm, curation) = Make(temp);

        vm.IsFavorite.ShouldBeFalse();
        vm.ToggleFavoriteCommand.Execute(null);
        vm.IsFavorite.ShouldBeTrue();

        // Verify persisted via a fresh CurationRepository read
        var fresh = new CurationRepository(temp.Db);
        fresh.IsFavorite(vm.VideoId).ShouldBeTrue();

        vm.ToggleFavoriteCommand.Execute(null);
        vm.IsFavorite.ShouldBeFalse();
        fresh.IsFavorite(vm.VideoId).ShouldBeFalse();
    }

    [Fact]
    public void SetRating_persists_and_clamps()
    {
        using var temp = new AppTempDb();
        var (vm, curation) = Make(temp);

        vm.SetRatingCommand.Execute("3");
        vm.Rating.ShouldBe(3.0);

        // Verify persisted
        var fresh = new CurationRepository(temp.Db);
        fresh.GetRating(vm.VideoId).ShouldBe(3.0);

        // Clamp above 5
        vm.SetRatingCommand.Execute("99");
        vm.Rating.ShouldBe(5.0);
        fresh.GetRating(vm.VideoId).ShouldBe(5.0);

        // Clamp below 0
        vm.SetRatingCommand.Execute("-5");
        vm.Rating.ShouldBe(0.0);
        fresh.GetRating(vm.VideoId).ShouldBe(0.0);
    }

    [Fact]
    public void SetRating_persists_half_star()
    {
        using var temp = new AppTempDb();
        var (vm, curation) = Make(temp);

        vm.SetRatingCommand.Execute("3.5");
        vm.Rating.ShouldBe(3.5);

        var fresh = new CurationRepository(temp.Db);
        fresh.GetRating(vm.VideoId).ShouldBe(3.5);
    }

    [Fact]
    public void ToggleWatchlist_flips_flag_and_persists()
    {
        using var temp = new AppTempDb();
        var (vm, curation) = Make(temp);

        vm.InWatchlist.ShouldBeFalse();
        vm.ToggleWatchlistCommand.Execute(null);
        vm.InWatchlist.ShouldBeTrue();

        // Verify persisted via a fresh CurationRepository read
        var fresh = new CurationRepository(temp.Db);
        fresh.InWatchlist(vm.VideoId).ShouldBeTrue();

        vm.ToggleWatchlistCommand.Execute(null);
        vm.InWatchlist.ShouldBeFalse();
        fresh.InWatchlist(vm.VideoId).ShouldBeFalse();
    }

    [Fact]
    public void HasCuration_is_false_when_no_curation_injected()
    {
        using var temp = new AppTempDb();
        var lib = new LibraryRepository(temp.Db);
        var ser = lib.UpsertSeries(lib.UpsertSection(lib.UpsertSource(@"C:\V", "V"), "S"), "Base", false);
        var videoId = lib.UpsertVideo(ser, @"C:\V\S\a.mp4", 1, ".mp4");
        var watch = new WatchRepository(temp.Db);
        var view = new EpisodeView(videoId, 1, @"C:\V\S\a.mp4", 1, "Base", false, false);
        var vm = new EpisodeViewModel(view, watch);

        vm.HasCuration.ShouldBeFalse();
    }

    [Fact]
    public void HasCuration_is_true_when_curation_injected()
    {
        using var temp = new AppTempDb();
        var (vm, _) = Make(temp);

        vm.HasCuration.ShouldBeTrue();
    }
}
