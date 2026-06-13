using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Shouldly;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Storage;
using Xunit;

namespace VideoShelf.App.Tests.ViewModels;

public sealed class SeriesTagWiringTests
{
    // ── Fixture helpers ──────────────────────────────────────────────────────

    private sealed class NullThumbs : VideoShelf.App.Services.IThumbnailService
    {
        public Task<string?> GetThumbnailPathAsync(string videoPath, CancellationToken ct)
            => Task.FromResult<string?>(null);
    }

    private sealed record Fx(
        AppTempDb Db,
        LibraryRepository Lib,
        TagRepository Tags,
        WatchRepository Watch,
        long SectionId,
        long SeriesId,
        long VideoId,
        VideoShelf.Core.Models.SeriesSummary Summary);

    private static Fx NewFx()
    {
        var db = new AppTempDb();
        var lib = new LibraryRepository(db.Db);
        var tags = new TagRepository(db.Db);
        var watch = new WatchRepository(db.Db);

        var srcId = lib.UpsertSource(@"C:\vids", "V");
        var sectionId = lib.UpsertSection(srcId, "Creator A");
        var seriesId = lib.UpsertSeries(sectionId, "Show A", isStandalone: false);
        var videoId = lib.UpsertVideo(seriesId, @"C:\vids\Show A\ep01.mkv", 1, "mkv");

        var summary = lib.GetSeriesSummaries(sectionId).Single();
        return new Fx(db, lib, tags, watch, sectionId, seriesId, videoId, summary);
    }

    // ── Back-compat: 4-arg ctor leaves SeriesTagEditor null ─────────────────

    [Fact]
    public void SeriesViewModel_without_tags_has_null_SeriesTagEditor()
    {
        var f = NewFx(); using var _d = f.Db;
        var vm = new SeriesViewModel(f.Summary, f.Lib, f.Watch, new NullThumbs());
        vm.SeriesTagEditor.ShouldBeNull();
    }

    // ── With tags: editor created ────────────────────────────────────────────

    [Fact]
    public void SeriesViewModel_with_tags_has_non_null_SeriesTagEditor()
    {
        var f = NewFx(); using var _d = f.Db;
        var vm = new SeriesViewModel(f.Summary, f.Lib, f.Watch, new NullThumbs(), f.Tags);
        vm.SeriesTagEditor.ShouldNotBeNull();
    }

    // ── After expand: SeriesTagEditor is loaded and reflects DB ─────────────

    [Fact]
    public async Task After_expand_SeriesTagEditor_is_loaded_and_reflects_series_tags()
    {
        var f = NewFx(); using var _d = f.Db;
        f.Tags.AddTag(f.SectionId, "comedy");        // section-level → inherited by series
        f.Tags.AddSeriesTag(f.SeriesId, "thriller"); // applied directly to series

        var vm = new SeriesViewModel(f.Summary, f.Lib, f.Watch, new NullThumbs(), f.Tags);

        // Activate triggers EnsureEpisodesLoadedAsync which calls Load on the current (test) thread
        await vm.ActivateCommand.ExecuteAsync(null);

        vm.SeriesTagEditor.ShouldNotBeNull();
        vm.SeriesTagEditor!.Tags.ShouldContain("thriller");
        vm.SeriesTagEditor.Inherited.ShouldContain(x => x.Tag == "comedy" && x.SourceLabel == "from Creator");
    }

    // ── After expand: each episode has a loaded VideoTagEditor ───────────────

    [Fact]
    public async Task After_expand_each_episode_has_loaded_VideoTagEditor()
    {
        var f = NewFx(); using var _d = f.Db;
        f.Tags.AddVideoTag(f.VideoId, "action");

        var vm = new SeriesViewModel(f.Summary, f.Lib, f.Watch, new NullThumbs(), f.Tags);
        await vm.ActivateCommand.ExecuteAsync(null);

        var ep = vm.Episodes.Single();
        ep.VideoTagEditor.ShouldNotBeNull();
        ep.VideoTagEditor!.Tags.ShouldContain("action");
    }

    // ── EpisodeViewModel without tags has null VideoTagEditor ────────────────

    [Fact]
    public void EpisodeViewModel_without_tags_has_null_VideoTagEditor()
    {
        var f = NewFx(); using var _d = f.Db;
        var ep = f.Lib.GetEpisodes(f.SeriesId).Single();
        var epVm = new EpisodeViewModel(ep, f.Watch);
        epVm.VideoTagEditor.ShouldBeNull();
    }
}
