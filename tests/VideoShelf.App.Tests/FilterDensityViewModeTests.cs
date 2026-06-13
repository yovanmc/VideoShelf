using System.Threading;
using System.Threading.Tasks;
using Shouldly;
using VideoShelf.App.Services;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Scanning;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;
using Xunit;

namespace VideoShelf.App.Tests;

/// <summary>
/// F3 — Tests for Group F: in-page filter predicate, settings round-trips,
/// and density/view-mode persistence.
/// </summary>
public class FilterDensityViewModeTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private sealed class NullThumbs : IThumbnailService
    {
        public Task<string?> GetThumbnailPathAsync(string videoPath, CancellationToken ct)
            => Task.FromResult<string?>(null);
    }

    // ── SettingsRepository round-trips (F3a) ─────────────────────────────────

    [Fact]
    public void BrowseDensity_defaults_to_Normal_when_unset()
    {
        using var db = new AppTempDb();
        var settings = new SettingsRepository(db.Db);

        settings.GetBrowseDensity().ShouldBe(BrowseDensity.Normal);
    }

    [Fact]
    public void BrowseDensity_roundtrips_Compact()
    {
        using var db = new AppTempDb();
        var settings = new SettingsRepository(db.Db);

        settings.SetBrowseDensity(BrowseDensity.Compact);

        settings.GetBrowseDensity().ShouldBe(BrowseDensity.Compact);
    }

    [Fact]
    public void BrowseDensity_roundtrips_Spacious()
    {
        using var db = new AppTempDb();
        var settings = new SettingsRepository(db.Db);

        settings.SetBrowseDensity(BrowseDensity.Spacious);

        settings.GetBrowseDensity().ShouldBe(BrowseDensity.Spacious);
    }

    [Fact]
    public void BrowseViewMode_defaults_to_Grid_when_unset()
    {
        using var db = new AppTempDb();
        var settings = new SettingsRepository(db.Db);

        settings.GetBrowseViewMode().ShouldBe(BrowseViewMode.Grid);
    }

    [Fact]
    public void BrowseViewMode_roundtrips_List()
    {
        using var db = new AppTempDb();
        var settings = new SettingsRepository(db.Db);

        settings.SetBrowseViewMode(BrowseViewMode.List);

        settings.GetBrowseViewMode().ShouldBe(BrowseViewMode.List);
    }

    [Fact]
    public void BrowseViewMode_roundtrips_back_to_Grid()
    {
        using var db = new AppTempDb();
        var settings = new SettingsRepository(db.Db);

        settings.SetBrowseViewMode(BrowseViewMode.List);
        settings.SetBrowseViewMode(BrowseViewMode.Grid);

        settings.GetBrowseViewMode().ShouldBe(BrowseViewMode.Grid);
    }

    // ── Creator filter predicate (F3b) ────────────────────────────────────────

    [Fact]
    public void CreatorFilter_empty_text_matches_everything()
    {
        using var db = new AppTempDb();
        var art = new CreatorArtRepository(db.Db);
        var lib = new LibraryRepository(db.Db);
        var src = lib.UpsertSource(@"C:\V", "V");
        var sec = lib.UpsertSection(src, "Creator A");
        var sum = new VideoShelf.Core.Models.SectionSummary(sec, src, "Creator A", 0, 0, 0, null);
        var card = new CreatorCardViewModel(sum, null, new NullThumbs());

        CreatorsViewModel.CreatorMatchesPredicate(card, "").ShouldBeTrue();
    }

    [Fact]
    public void CreatorFilter_whitespace_only_matches_everything()
    {
        using var db = new AppTempDb();
        var art = new CreatorArtRepository(db.Db);
        var lib = new LibraryRepository(db.Db);
        var src = lib.UpsertSource(@"C:\V", "V");
        var sec = lib.UpsertSection(src, "Creator A");
        var sum = new VideoShelf.Core.Models.SectionSummary(sec, src, "Creator A", 0, 0, 0, null);
        var card = new CreatorCardViewModel(sum, null, new NullThumbs());

        CreatorsViewModel.CreatorMatchesPredicate(card, "   ").ShouldBeTrue();
    }

    [Fact]
    public void CreatorFilter_matching_substring_case_insensitive_returns_true()
    {
        using var db = new AppTempDb();
        var lib = new LibraryRepository(db.Db);
        var src = lib.UpsertSource(@"C:\V", "V");
        var sec = lib.UpsertSection(src, "Studio Ghibli");
        var sum = new VideoShelf.Core.Models.SectionSummary(sec, src, "Studio Ghibli", 0, 0, 0, null);
        var card = new CreatorCardViewModel(sum, null, new NullThumbs());

        CreatorsViewModel.CreatorMatchesPredicate(card, "ghibli").ShouldBeTrue();
        CreatorsViewModel.CreatorMatchesPredicate(card, "GHIBLI").ShouldBeTrue();
        CreatorsViewModel.CreatorMatchesPredicate(card, "Studio").ShouldBeTrue();
    }

    [Fact]
    public void CreatorFilter_non_matching_text_returns_false()
    {
        using var db = new AppTempDb();
        var lib = new LibraryRepository(db.Db);
        var src = lib.UpsertSource(@"C:\V", "V");
        var sec = lib.UpsertSection(src, "Studio Ghibli");
        var sum = new VideoShelf.Core.Models.SectionSummary(sec, src, "Studio Ghibli", 0, 0, 0, null);
        var card = new CreatorCardViewModel(sum, null, new NullThumbs());

        CreatorsViewModel.CreatorMatchesPredicate(card, "Pixar").ShouldBeFalse();
    }

    // ── Series filter predicate (F3b) ────────────────────────────────────────

    [Fact]
    public void SeriesFilter_empty_text_matches_everything()
    {
        using var db = new AppTempDb();
        var lib = new LibraryRepository(db.Db);
        var watch = new WatchRepository(db.Db);
        var tags = new TagRepository(db.Db);
        var src = lib.UpsertSource(@"C:\V", "V");
        var sec = lib.UpsertSection(src, "Creator");
        var ser = lib.UpsertSeries(sec, "My Show", false);
        var sum = new VideoShelf.Core.Models.SeriesSummary(ser, sec, "My Show", false, 0, 0, null);
        var svm = new SeriesViewModel(sum, lib, watch, new NullThumbs());

        SectionDetailViewModel.SeriesMatchesPredicate(svm, "").ShouldBeTrue();
    }

    [Fact]
    public void SeriesFilter_matching_substring_case_insensitive_returns_true()
    {
        using var db = new AppTempDb();
        var lib = new LibraryRepository(db.Db);
        var watch = new WatchRepository(db.Db);
        var src = lib.UpsertSource(@"C:\V", "V");
        var sec = lib.UpsertSection(src, "Creator");
        var ser = lib.UpsertSeries(sec, "Fullmetal Alchemist", false);
        var sum = new VideoShelf.Core.Models.SeriesSummary(ser, sec, "Fullmetal Alchemist", false, 0, 0, null);
        var svm = new SeriesViewModel(sum, lib, watch, new NullThumbs());

        SectionDetailViewModel.SeriesMatchesPredicate(svm, "fullmetal").ShouldBeTrue();
        SectionDetailViewModel.SeriesMatchesPredicate(svm, "ALCHEMIST").ShouldBeTrue();
    }

    [Fact]
    public void SeriesFilter_non_matching_text_returns_false()
    {
        using var db = new AppTempDb();
        var lib = new LibraryRepository(db.Db);
        var watch = new WatchRepository(db.Db);
        var src = lib.UpsertSource(@"C:\V", "V");
        var sec = lib.UpsertSection(src, "Creator");
        var ser = lib.UpsertSeries(sec, "Fullmetal Alchemist", false);
        var sum = new VideoShelf.Core.Models.SeriesSummary(ser, sec, "Fullmetal Alchemist", false, 0, 0, null);
        var svm = new SeriesViewModel(sum, lib, watch, new NullThumbs());

        SectionDetailViewModel.SeriesMatchesPredicate(svm, "Naruto").ShouldBeFalse();
    }

    // ── VM density/view-mode load-from-settings + persist-on-change (F3c) ────

    [Fact]
    public void CreatorsViewModel_loads_density_from_settings()
    {
        using var db = new AppTempDb();
        var settings = new SettingsRepository(db.Db);
        settings.SetBrowseDensity(BrowseDensity.Compact);

        var lib = new LibraryRepository(db.Db);
        var art = new CreatorArtRepository(db.Db);
        var vm = new CreatorsViewModel(lib, art, new NullThumbs(), settings);

        vm.Density.ShouldBe(BrowseDensity.Compact);
    }

    [Fact]
    public void CreatorsViewModel_persists_density_change()
    {
        using var db = new AppTempDb();
        var settings = new SettingsRepository(db.Db);
        var lib = new LibraryRepository(db.Db);
        var art = new CreatorArtRepository(db.Db);
        var vm = new CreatorsViewModel(lib, art, new NullThumbs(), settings);

        vm.Density = BrowseDensity.Spacious;

        settings.GetBrowseDensity().ShouldBe(BrowseDensity.Spacious);
    }

    [Fact]
    public void CreatorsViewModel_loads_view_mode_from_settings()
    {
        using var db = new AppTempDb();
        var settings = new SettingsRepository(db.Db);
        settings.SetBrowseViewMode(BrowseViewMode.List);

        var lib = new LibraryRepository(db.Db);
        var art = new CreatorArtRepository(db.Db);
        var vm = new CreatorsViewModel(lib, art, new NullThumbs(), settings);

        vm.ViewMode.ShouldBe(BrowseViewMode.List);
    }

    [Fact]
    public void CreatorsViewModel_persists_view_mode_change()
    {
        using var db = new AppTempDb();
        var settings = new SettingsRepository(db.Db);
        var lib = new LibraryRepository(db.Db);
        var art = new CreatorArtRepository(db.Db);
        var vm = new CreatorsViewModel(lib, art, new NullThumbs(), settings);

        vm.ViewMode = BrowseViewMode.List;

        settings.GetBrowseViewMode().ShouldBe(BrowseViewMode.List);
    }

    [Fact]
    public void CreatorsViewModel_without_settings_uses_defaults()
    {
        using var db = new AppTempDb();
        var lib = new LibraryRepository(db.Db);
        var art = new CreatorArtRepository(db.Db);
        var vm = new CreatorsViewModel(lib, art, new NullThumbs());

        // No settings injected — should still produce defaults without throwing.
        vm.Density.ShouldBe(BrowseDensity.Normal);
        vm.ViewMode.ShouldBe(BrowseViewMode.Grid);
    }
}
