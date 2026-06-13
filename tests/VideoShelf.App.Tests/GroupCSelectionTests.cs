using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using Shouldly;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using VideoShelf.App.ViewModels.Discovery;
using VideoShelf.Core.Discovery;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;
using Xunit;

namespace VideoShelf.App.Tests;

/// <summary>
/// C3 — Multi-select tests for Group C:
///   • EpisodeViewModel.IsSelected round-trips and notifies.
///   • RecencyCardViewModel.IsSelected round-trips and notifies.
///   • Per-page selection wiring (FavoritesViewModel, WatchlistViewModel,
///     SearchViewModel, SectionDetailViewModel).
///   • Active-source switching (MainViewModel.BulkBarVisible) with regression
///     that navigating away from a selected page makes BulkBarVisible false.
/// </summary>
public class GroupCSelectionTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static (WatchRepository watch, long videoId) SeedVideo(AppTempDb temp)
    {
        var lib = new LibraryRepository(temp.Db);
        var ser = lib.UpsertSeries(
            lib.UpsertSection(lib.UpsertSource(@"C:\V", "V"), "S"), "Base", false);
        var vid = lib.UpsertVideo(ser, @"C:\V\S\a.mp4", 1, ".mp4");
        return (new WatchRepository(temp.Db), vid);
    }

    private static EpisodeViewModel MakeEpisode(long videoId, WatchRepository watch)
    {
        var view = new EpisodeView(videoId, 1, @"C:\V\S\a.mp4", 1, "Base", Watched: false, Missing: false);
        return new EpisodeViewModel(view, watch);
    }

    // ── C3a: EpisodeViewModel.IsSelected ─────────────────────────────────────

    [Fact]
    public void EpisodeViewModel_IsSelected_defaults_to_false()
    {
        using var temp = new AppTempDb();
        var (watch, videoId) = SeedVideo(temp);
        var ep = MakeEpisode(videoId, watch);

        ep.IsSelected.ShouldBeFalse();
    }

    [Fact]
    public void EpisodeViewModel_IsSelected_can_be_set_to_true()
    {
        using var temp = new AppTempDb();
        var (watch, videoId) = SeedVideo(temp);
        var ep = MakeEpisode(videoId, watch);

        ep.IsSelected = true;

        ep.IsSelected.ShouldBeTrue();
    }

    [Fact]
    public void EpisodeViewModel_IsSelected_raises_PropertyChanged()
    {
        using var temp = new AppTempDb();
        var (watch, videoId) = SeedVideo(temp);
        var ep = MakeEpisode(videoId, watch);

        var raised = new List<string?>();
        ep.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        ep.IsSelected = true;

        raised.ShouldContain(nameof(EpisodeViewModel.IsSelected));
    }

    [Fact]
    public void EpisodeViewModel_IsSelected_round_trips()
    {
        using var temp = new AppTempDb();
        var (watch, videoId) = SeedVideo(temp);
        var ep = MakeEpisode(videoId, watch);

        ep.IsSelected = true;
        ep.IsSelected = false;

        ep.IsSelected.ShouldBeFalse();
    }

    [Fact]
    public void EpisodeViewModel_implements_ISelectableCard()
    {
        using var temp = new AppTempDb();
        var (watch, videoId) = SeedVideo(temp);
        var ep = MakeEpisode(videoId, watch);

        (ep as ISelectableCard).ShouldNotBeNull();
    }

    // ── C3b: RecencyCardViewModel.IsSelected ─────────────────────────────────

    private static RecencyCardViewModel MakeRecencyCard(long videoId)
    {
        // RecencyItem(VideoId, SeriesId, SectionId, SeriesTitle, IsStandalone, EpisodeNo, Watched, ThumbnailSeedPath)
        var item = new RecencyItem(videoId, 1L, 1L, "Base", IsStandalone: true, EpisodeNo: 1, Watched: false, ThumbnailSeedPath: null);
        return new RecencyCardViewModel(item);
    }

    [Fact]
    public void RecencyCardViewModel_IsSelected_defaults_to_false()
    {
        using var temp = new AppTempDb();
        var (_, videoId) = SeedVideo(temp);
        var card = MakeRecencyCard(videoId);

        card.IsSelected.ShouldBeFalse();
    }

    [Fact]
    public void RecencyCardViewModel_IsSelected_can_be_set_to_true()
    {
        using var temp = new AppTempDb();
        var (_, videoId) = SeedVideo(temp);
        var card = MakeRecencyCard(videoId);

        card.IsSelected = true;

        card.IsSelected.ShouldBeTrue();
    }

    [Fact]
    public void RecencyCardViewModel_IsSelected_raises_PropertyChanged()
    {
        using var temp = new AppTempDb();
        var (_, videoId) = SeedVideo(temp);
        var card = MakeRecencyCard(videoId);

        var raised = new List<string?>();
        card.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        card.IsSelected = true;

        raised.ShouldContain(nameof(RecencyCardViewModel.IsSelected));
    }

    [Fact]
    public void RecencyCardViewModel_implements_ISelectableCard()
    {
        using var temp = new AppTempDb();
        var (_, videoId) = SeedVideo(temp);
        var card = MakeRecencyCard(videoId);

        (card as ISelectableCard).ShouldNotBeNull();
    }

    // ── C3c: FavoritesViewModel selection wiring ──────────────────────────────

    [Fact]
    public async Task FavoritesViewModel_selecting_card_adds_videoId_to_GetSelectedVideoIds()
    {
        using var temp = new AppTempDb();
        var lib = new LibraryRepository(temp.Db);
        var ser = lib.UpsertSeries(lib.UpsertSection(lib.UpsertSource(@"C:\V", "V"), "S"), "Base", false);
        var vid = lib.UpsertVideo(ser, @"C:\V\S\a.mp4", 1, ".mp4");
        var curation = new CurationRepository(temp.Db);
        curation.SetFavorite(vid, true);

        var vm = new FavoritesViewModel(curation, lib);
        await vm.LoadAsync();
        vm.Favorites.Count.ShouldBe(1);

        vm.Favorites[0].IsSelected = true;

        vm.GetSelectedVideoIds().ShouldContain(vid);
        vm.Selection.HasSelection.ShouldBeTrue();
    }

    [Fact]
    public async Task FavoritesViewModel_deselecting_card_removes_from_GetSelectedVideoIds()
    {
        using var temp = new AppTempDb();
        var lib = new LibraryRepository(temp.Db);
        var ser = lib.UpsertSeries(lib.UpsertSection(lib.UpsertSource(@"C:\V", "V"), "S"), "Base", false);
        var vid = lib.UpsertVideo(ser, @"C:\V\S\a.mp4", 1, ".mp4");
        var curation = new CurationRepository(temp.Db);
        curation.SetFavorite(vid, true);

        var vm = new FavoritesViewModel(curation, lib);
        await vm.LoadAsync();

        vm.Favorites[0].IsSelected = true;
        vm.Favorites[0].IsSelected = false;

        vm.GetSelectedVideoIds().ShouldBeEmpty();
        vm.Selection.HasSelection.ShouldBeFalse();
    }

    // ── C3d: WatchlistViewModel selection wiring ──────────────────────────────

    [Fact]
    public async Task WatchlistViewModel_selecting_card_adds_videoId_to_GetSelectedVideoIds()
    {
        using var temp = new AppTempDb();
        var lib = new LibraryRepository(temp.Db);
        var ser = lib.UpsertSeries(lib.UpsertSection(lib.UpsertSource(@"C:\V", "V"), "S"), "Base", false);
        var vid = lib.UpsertVideo(ser, @"C:\V\S\a.mp4", 1, ".mp4");
        var curation = new CurationRepository(temp.Db);
        curation.SetWatchlist(vid, true, DateTimeOffset.UtcNow);

        var vm = new WatchlistViewModel(curation, lib);
        await vm.LoadAsync();
        vm.Watchlist.Count.ShouldBe(1);

        vm.Watchlist[0].IsSelected = true;

        vm.GetSelectedVideoIds().ShouldContain(vid);
        vm.Selection.HasSelection.ShouldBeTrue();
    }

    // ── C3e: SearchViewModel selection wiring ─────────────────────────────────

    [Fact]
    public async Task SearchViewModel_selecting_videoResult_card_adds_videoId_to_GetSelectedVideoIds()
    {
        using var temp = new AppTempDb();
        var lib = new LibraryRepository(temp.Db);
        var srcId = lib.UpsertSource(@"C:\V", "V");
        var sectionId = lib.UpsertSection(srcId, "NatGeo");
        var seriesId = lib.UpsertSeries(sectionId, "NatGeo Documentary", false);
        var vid = lib.UpsertVideo(seriesId, @"C:\V\NatGeo\e01.mp4", 1, ".mp4");

        var art = new CreatorArtRepository(temp.Db);
        var cardFactory = new CreatorCardFactory(art, new NullThumbs());
        var vm = new SearchViewModel(lib, cardFactory);

        vm.Query = "documentary";
        await vm.WaitForIdleAsync();
        vm.VideoResults.Count.ShouldBeGreaterThanOrEqualTo(1);

        vm.VideoResults[0].IsSelected = true;

        vm.GetSelectedVideoIds().ShouldContain(vid);
        vm.Selection.HasSelection.ShouldBeTrue();
    }

    // ── C3f: Active-source switching + BulkBarVisible regression ─────────────

    [Fact]
    public void MainViewModel_BulkBarVisible_false_initially()
    {
        var vm = MainViewModelTestFactory.Create(out var ctx);
        using var _ = ctx.Db;

        vm.BulkBarVisible.ShouldBeFalse();
    }

    [Fact]
    public void MainViewModel_BulkBarVisible_false_on_non_selectable_page()
    {
        var vm = MainViewModelTestFactory.Create(out var ctx);
        using var _ = ctx.Db;

        vm.ShowSettingsCommand.Execute(null);

        vm.ActiveSelectionSource.ShouldBeNull();
        vm.BulkBarVisible.ShouldBeFalse();
    }

    [Fact]
    public async Task MainViewModel_BulkBarVisible_true_when_Favorites_selection_is_non_empty()
    {
        var vm = MainViewModelTestFactory.Create(out var ctx);
        using var _ = ctx.Db;
        // Seed a favorite using the factory's DB.
        var lib = new LibraryRepository(ctx.Db.Db);
        var curation = new CurationRepository(ctx.Db.Db);
        // Use the section that was seeded by the factory.
        var series = lib.GetSeriesForSection(ctx.SectionId);
        var videos = lib.GetVideosForSeries(series[0].Id);
        curation.SetFavorite(videos[0].Id, true);

        vm.ShowFavoritesCommand.Execute(null);
        await vm.Favorites.LoadAsync();

        vm.Favorites.Favorites.Count.ShouldBeGreaterThanOrEqualTo(1);
        vm.Favorites.Favorites[0].IsSelected = true;

        vm.BulkBarVisible.ShouldBeTrue();
    }

    [Fact]
    public async Task MainViewModel_BulkBarVisible_becomes_false_when_navigating_away_from_Favorites_with_selection()
    {
        var vm = MainViewModelTestFactory.Create(out var ctx);
        using var _ = ctx.Db;
        // Seed a favorite.
        var lib = new LibraryRepository(ctx.Db.Db);
        var curation = new CurationRepository(ctx.Db.Db);
        var series = lib.GetSeriesForSection(ctx.SectionId);
        var videos = lib.GetVideosForSeries(series[0].Id);
        curation.SetFavorite(videos[0].Id, true);

        vm.ShowFavoritesCommand.Execute(null);
        await vm.Favorites.LoadAsync();
        vm.Favorites.Favorites[0].IsSelected = true;
        vm.BulkBarVisible.ShouldBeTrue();

        // Navigate away — BulkBarVisible must become false.
        vm.ShowHomeCommand.Execute(null);

        vm.BulkBarVisible.ShouldBeFalse();
        vm.ActiveSelectionSource.ShouldBeNull();
        // The Favorites selection should also be cleared.
        vm.Favorites.Selection.HasSelection.ShouldBeFalse();
    }

    [Fact]
    public void MainViewModel_ActiveSelectionSource_is_Creators_when_on_Browse()
    {
        var vm = MainViewModelTestFactory.Create(out var ctx);
        using var _ = ctx.Db;

        vm.ShowBrowseCommand.Execute(null);

        vm.ActiveSelectionSource.ShouldBeSameAs(vm.Creators);
    }

    [Fact]
    public void MainViewModel_ActiveSelectionSource_is_Favorites_when_on_Favorites()
    {
        var vm = MainViewModelTestFactory.Create(out var ctx);
        using var _ = ctx.Db;

        vm.ShowFavoritesCommand.Execute(null);

        vm.ActiveSelectionSource.ShouldBeSameAs(vm.Favorites);
    }

    [Fact]
    public void MainViewModel_ActiveSelectionSource_is_Watchlist_when_on_Watchlist()
    {
        var vm = MainViewModelTestFactory.Create(out var ctx);
        using var _ = ctx.Db;

        vm.ShowWatchlistCommand.Execute(null);

        vm.ActiveSelectionSource.ShouldBeSameAs(vm.Watchlist);
    }

    [Fact]
    public void MainViewModel_ActiveSelectionSource_is_Search_when_on_Search()
    {
        var vm = MainViewModelTestFactory.Create(out var ctx);
        using var _ = ctx.Db;

        // Search view is navigated to by setting Query; simulate that.
        // Or just set CurrentView directly since the ctor doesn't restrict this in tests.
        vm.CurrentView = AppView.Search;

        vm.ActiveSelectionSource.ShouldBeSameAs(vm.Search);
    }

    [Fact]
    public void MainViewModel_ActiveSelectionSource_is_SectionDetail_when_on_SectionDetail()
    {
        var vm = MainViewModelTestFactory.Create(out var ctx);
        using var _ = ctx.Db;

        vm.CurrentView = AppView.SectionDetail;

        vm.ActiveSelectionSource.ShouldBeSameAs(vm.SectionDetail);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private sealed class NullThumbs : VideoShelf.App.Services.IThumbnailService
    {
        public Task<string?> GetThumbnailPathAsync(string videoPath, System.Threading.CancellationToken ct)
            => Task.FromResult<string?>(null);
    }
}
