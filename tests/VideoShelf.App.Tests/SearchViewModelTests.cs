using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Shouldly;
using VideoShelf.App.Services;
using VideoShelf.App.ViewModels;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;
using Xunit;

namespace VideoShelf.App.Tests;

public sealed class SearchViewModelTests
{
    private sealed class NullThumbs : IThumbnailService
    {
        public Task<string?> GetThumbnailPathAsync(string videoPath, CancellationToken ct)
            => Task.FromResult<string?>(null);
    }

    private sealed record Fx(AppTempDb Db, LibraryRepository Lib, SearchViewModel Vm)
        : IDisposable
    {
        public void Dispose() => Db.Dispose();
    }

    private static Fx NewFx()
    {
        var db = new AppTempDb();
        var lib = new LibraryRepository(db.Db);
        var art = new CreatorArtRepository(db.Db);
        var thumbs = new NullThumbs();
        var cardFactory = new CreatorCardFactory(art, thumbs);
        var vm = new SearchViewModel(lib, cardFactory);
        return new Fx(db, lib, vm);
    }

    private static (long sectionId, long seriesId, long videoId) SeedNatGeo(LibraryRepository lib)
    {
        var srcId = lib.UpsertSource(@"C:\V", "V");
        var sectionId = lib.UpsertSection(srcId, "NatGeo");
        var seriesId = lib.UpsertSeries(sectionId, "NatGeo Documentary", false);
        var videoId = lib.UpsertVideo(seriesId, @"C:\V\NatGeo\e01.mp4", 1, ".mp4");
        return (sectionId, seriesId, videoId);
    }

    [Fact]
    public async Task Query_matching_creator_name_populates_CreatorResults()
    {
        using var f = NewFx();
        var (sectionId, _, _) = SeedNatGeo(f.Lib);

        f.Vm.Query = "nat";
        await f.Vm.WaitForIdleAsync();

        f.Vm.CreatorResults.Count.ShouldBeGreaterThanOrEqualTo(1);
        f.Vm.HasCreatorResults.ShouldBeTrue();
        f.Vm.CreatorResults.ShouldContain(c => c.Name.Contains("NatGeo"));
    }

    [Fact]
    public async Task Query_matching_video_series_populates_VideoResults()
    {
        using var f = NewFx();
        SeedNatGeo(f.Lib);

        // "documentary" matches the series base_title
        f.Vm.Query = "documentary";
        await f.Vm.WaitForIdleAsync();

        f.Vm.VideoResults.Count.ShouldBeGreaterThanOrEqualTo(1);
        f.Vm.HasVideoResults.ShouldBeTrue();
    }

    [Fact]
    public async Task Clearing_query_empties_both_collections()
    {
        using var f = NewFx();
        SeedNatGeo(f.Lib);

        f.Vm.Query = "nat";
        await f.Vm.WaitForIdleAsync();
        f.Vm.CreatorResults.Count.ShouldBeGreaterThanOrEqualTo(1);

        f.Vm.Query = "";
        await f.Vm.WaitForIdleAsync();

        f.Vm.CreatorResults.Count.ShouldBe(0);
        f.Vm.VideoResults.Count.ShouldBe(0);
        f.Vm.HasQuery.ShouldBeFalse();
        f.Vm.NoResults.ShouldBeFalse();
    }

    [Fact]
    public async Task Video_card_Play_raises_PlayRequested_with_correct_episode()
    {
        using var f = NewFx();
        var (_, _, videoId) = SeedNatGeo(f.Lib);

        f.Vm.Query = "documentary";
        await f.Vm.WaitForIdleAsync();
        f.Vm.VideoResults.Count.ShouldBeGreaterThanOrEqualTo(1);

        EpisodeView? played = null;
        f.Vm.PlayRequested += (_, e) => played = e;

        f.Vm.VideoResults[0].PlayCommand.Execute(null);

        played.ShouldNotBeNull();
        played!.VideoId.ShouldBe(videoId);
    }

    [Fact]
    public async Task Creator_card_Open_raises_OpenCreatorRequested_with_section_id()
    {
        using var f = NewFx();
        var (sectionId, _, _) = SeedNatGeo(f.Lib);

        f.Vm.Query = "nat";
        await f.Vm.WaitForIdleAsync();
        f.Vm.CreatorResults.Count.ShouldBeGreaterThanOrEqualTo(1);

        var openedIds = new List<long>();
        f.Vm.OpenCreatorRequested += id => openedIds.Add(id);

        f.Vm.CreatorResults[0].OpenCommand.Execute(null);

        openedIds.ShouldContain(sectionId);
    }

    [Fact]
    public async Task Query_matching_series_title_populates_SeriesResults()
    {
        using var f = NewFx();
        SeedNatGeo(f.Lib);

        // "documentary" matches the series base_title
        f.Vm.Query = "documentary";
        await f.Vm.WaitForIdleAsync();

        f.Vm.SeriesResults.Count.ShouldBeGreaterThanOrEqualTo(1);
        f.Vm.HasSeriesResults.ShouldBeTrue();
        f.Vm.SeriesResults.ShouldContain(s => s.Title.Contains("Documentary"));
    }

    [Fact]
    public async Task Series_card_Open_raises_OpenCreatorRequested_with_section_id()
    {
        using var f = NewFx();
        var (sectionId, _, _) = SeedNatGeo(f.Lib);

        f.Vm.Query = "documentary";
        await f.Vm.WaitForIdleAsync();
        f.Vm.SeriesResults.Count.ShouldBeGreaterThanOrEqualTo(1);

        var openedIds = new List<long>();
        f.Vm.OpenCreatorRequested += id => openedIds.Add(id);

        f.Vm.SeriesResults[0].OpenCommand.Execute(null);

        openedIds.ShouldContain(sectionId);
    }

    [Fact]
    public async Task ResultSummary_reflects_all_group_counts()
    {
        using var f = NewFx();
        SeedNatGeo(f.Lib);

        // "nat" matches both the creator ("NatGeo") and series ("NatGeo Documentary")
        f.Vm.Query = "nat";
        await f.Vm.WaitForIdleAsync();

        f.Vm.ResultSummary.ShouldContain("creator");
        f.Vm.ResultSummary.ShouldContain("series");
        f.Vm.ResultSummary.ShouldContain("video");
        f.Vm.HasResults.ShouldBeTrue();
    }

    [Fact]
    public async Task Clearing_query_empties_series_results_too()
    {
        using var f = NewFx();
        SeedNatGeo(f.Lib);

        f.Vm.Query = "documentary";
        await f.Vm.WaitForIdleAsync();
        f.Vm.SeriesResults.Count.ShouldBeGreaterThanOrEqualTo(1);

        f.Vm.Query = "";
        await f.Vm.WaitForIdleAsync();

        f.Vm.SeriesResults.Count.ShouldBe(0);
        f.Vm.HasSeriesResults.ShouldBeFalse();
    }

    [Fact]
    public async Task NoResults_is_true_when_no_creators_series_or_videos_match()
    {
        using var f = NewFx();
        SeedNatGeo(f.Lib);

        f.Vm.Query = "xyzzy_no_match_expected";
        await f.Vm.WaitForIdleAsync();

        f.Vm.NoResults.ShouldBeTrue();
        f.Vm.HasResults.ShouldBeFalse();
    }
}
