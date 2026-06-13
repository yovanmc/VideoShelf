using System.Threading;
using System.Threading.Tasks;
using Shouldly;
using VideoShelf.App.Services;
using VideoShelf.App.ViewModels;
using VideoShelf.App.ViewModels.Discovery;
using VideoShelf.Core.Discovery;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;
using VideoShelf.App.Tests.TestSupport;
using Xunit;

namespace VideoShelf.App.Tests;

public sealed class DiscoveryViewModelTests
{
    private sealed class NullThumbs : IThumbnailService
    {
        public Task<string?> GetThumbnailPathAsync(string videoPath, CancellationToken ct)
            => Task.FromResult<string?>(null);
    }

    private sealed record Fx(AppTempDb Db, LibraryRepository Lib, WatchRepository Watch,
        TagRepository Tags, DiscoveryRepository Disc, StatsRepository Stats, DiscoveryViewModel Vm,
        PlayQueueViewModel PlayQueue);

    private static Fx NewFx()
    {
        var db = new AppTempDb();
        var lib = new LibraryRepository(db.Db);
        var watch = new WatchRepository(db.Db);
        var tags = new TagRepository(db.Db);
        var disc = new DiscoveryRepository(db.Db, lib, tags);
        var statsRepo = new StatsRepository(db.Db);
        var settings = new SettingsRepository(db.Db);
        var art = new CreatorArtRepository(db.Db);
        var thumbs = new NullThumbs();
        var cardFactory = new CreatorCardFactory(art, thumbs);
        var playQueue = new PlayQueueViewModel(lib, settings);
        var vm = new DiscoveryViewModel(disc, lib, tags, cardFactory, statsRepo, playQueue);
        return new Fx(db, lib, watch, tags, disc, statsRepo, vm, playQueue);
    }

    [Fact]
    public async Task LoadAsync_populates_continue_watching_rail()
    {
        var f = NewFx(); using var _d = f.Db;
        var src = f.Lib.UpsertSource(@"C:\m", "M");
        var sec = f.Lib.UpsertSection(src, "S");
        var ser = f.Lib.UpsertSeries(sec, "Show", false);
        var vid = f.Lib.UpsertVideo(ser, @"C:\m\Show\e01.mkv", 1, "mkv");
        f.Lib.SetResumePosition(vid, 30);

        await f.Vm.LoadAsync();

        f.Vm.ContinueWatching.Count.ShouldBe(1);
        f.Vm.HasContinueWatching.ShouldBeTrue();
        f.Vm.ContinueWatching[0].VideoId.ShouldBe(vid);
    }

    [Fact]
    public async Task Continue_card_Play_raises_PlayRequested_with_matching_episode()
    {
        var f = NewFx(); using var _d = f.Db;
        var src = f.Lib.UpsertSource(@"C:\m", "M");
        var sec = f.Lib.UpsertSection(src, "S");
        var ser = f.Lib.UpsertSeries(sec, "Show", false);
        var vid = f.Lib.UpsertVideo(ser, @"C:\m\Show\e01.mkv", 1, "mkv");
        f.Lib.SetResumePosition(vid, 30);
        await f.Vm.LoadAsync();

        EpisodeView? played = null;
        f.Vm.PlayRequested += (_, e) => played = e;
        f.Vm.ContinueWatching[0].PlayCommand.Execute(null);

        played.ShouldNotBeNull();
        played!.VideoId.ShouldBe(vid);
    }

    [Fact]
    public async Task RecommendedCreators_card_Open_raises_SectionOpenRequested()
    {
        var f = NewFx(); using var _d = f.Db;
        var src = f.Lib.UpsertSource(@"C:\m", "M");
        var watchedSec = f.Lib.UpsertSection(src, "Watched");
        var candidate = f.Lib.UpsertSection(src, "Candidate");
        f.Tags.AddTag(watchedSec, "comedy");
        f.Tags.AddTag(candidate, "comedy");
        var ser = f.Lib.UpsertSeries(watchedSec, "WShow", false);
        var wv = f.Lib.UpsertVideo(ser, @"C:\m\WShow\e01.mkv", 1, "mkv");
        f.Lib.UpsertVideo(f.Lib.UpsertSeries(candidate, "CShow", false), @"C:\m\CShow\e01.mkv", 1, "mkv");
        f.Watch.SetWatched(wv, true);

        await f.Vm.LoadAsync();
        f.Vm.RecommendedCreators.ShouldNotBeEmpty();
        f.Vm.HasRecommendedCreators.ShouldBeTrue();

        long? opened = null;
        f.Vm.SectionOpenRequested += (_, id) => opened = id;
        f.Vm.RecommendedCreators[0].OpenCommand.Execute(null);
        opened.ShouldBe(candidate);
    }

    [Fact]
    public async Task RecommendedCreators_items_are_CreatorCardViewModel()
    {
        var f = NewFx(); using var _d = f.Db;
        var src = f.Lib.UpsertSource(@"C:\m", "M");
        var watchedSec = f.Lib.UpsertSection(src, "Watched");
        var candidate = f.Lib.UpsertSection(src, "Candidate");
        f.Tags.AddTag(watchedSec, "action");
        f.Tags.AddTag(candidate, "action");
        var ser = f.Lib.UpsertSeries(watchedSec, "WShow", false);
        var wv = f.Lib.UpsertVideo(ser, @"C:\m\WShow\e01.mkv", 1, "mkv");
        f.Lib.UpsertVideo(f.Lib.UpsertSeries(candidate, "CShow", false), @"C:\m\CShow\e01.mkv", 1, "mkv");
        f.Watch.SetWatched(wv, true);

        await f.Vm.LoadAsync();

        f.Vm.RecommendedCreators.ShouldNotBeEmpty();
        f.Vm.RecommendedCreators[0].ShouldBeOfType<CreatorCardViewModel>();
    }

    [Fact]
    public async Task ToggleTag_fills_TagResults_with_CreatorCardViewModel()
    {
        var f = NewFx(); using var _d = f.Db;
        var src = f.Lib.UpsertSource(@"C:\m", "M");
        var s1 = f.Lib.UpsertSection(src, "One");
        f.Tags.AddTag(s1, "comedy");
        f.Lib.UpsertVideo(f.Lib.UpsertSeries(s1, "OneShow", false), @"C:\m\OneShow\e01.mkv", 1, "mkv");

        await f.Vm.LoadAsync();
        var chip = f.Vm.AvailableTags.First(t => t.Tag == "comedy");
        await f.Vm.ToggleTagCommand.ExecuteAsync(chip);

        chip.IsSelected.ShouldBeTrue();
        f.Vm.TagResults.ShouldNotBeEmpty();
        f.Vm.TagResults[0].ShouldBeOfType<CreatorCardViewModel>();
        f.Vm.TagResults.Select(r => r.SectionId).ShouldContain(s1);
    }

    [Fact]
    public async Task Stats_populate_on_load()
    {
        var f = NewFx(); using var _d = f.Db;
        var src = f.Lib.UpsertSource(@"C:\m", "M");
        var sec = f.Lib.UpsertSection(src, "Creator A");
        var ser = f.Lib.UpsertSeries(sec, "ShowA", false);
        var vid1 = f.Lib.UpsertVideo(ser, @"C:\m\ShowA\e01.mkv", 1, "mkv");
        var vid2 = f.Lib.UpsertVideo(ser, @"C:\m\ShowA\e02.mkv", 2, "mkv");
        f.Lib.SetDuration(vid1, 3600);
        f.Watch.SetWatched(vid1, true);
        // vid2 is present but not watched

        await f.Vm.LoadAsync();

        f.Vm.HasStats.ShouldBeTrue();
        f.Vm.WatchedSummary.ShouldContain("watched");
        f.Vm.TopCreators.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Continue_card_shows_chapter_for_resume_position()
    {
        var f = NewFx(); using var _d = f.Db;
        var src = f.Lib.UpsertSource(@"C:\m", "M");
        var sec = f.Lib.UpsertSection(src, "S");
        var ser = f.Lib.UpsertSeries(sec, "Show", false);
        var vid = f.Lib.UpsertVideo(ser, @"C:\m\Show\e01.mkv", 1, "mkv");
        f.Lib.SetResumePosition(vid, 65);
        f.Lib.SetDuration(vid, 120);
        f.Lib.ReplaceChapters(vid, new ChapterRecord[]
        {
            new(0, "Intro", 0),
            new(1, "Part 2", 60),
        });

        await f.Vm.LoadAsync();

        f.Vm.ContinueWatching.Count.ShouldBe(1);
        var card = f.Vm.ContinueWatching[0];
        card.ChapterLabel.ShouldBe("Part 2");
        card.HasChapter.ShouldBeTrue();
    }

    [Fact]
    public async Task Continue_card_has_no_chapter_when_video_has_no_chapters()
    {
        var f = NewFx(); using var _d = f.Db;
        var src = f.Lib.UpsertSource(@"C:\m", "M");
        var sec = f.Lib.UpsertSection(src, "S");
        var ser = f.Lib.UpsertSeries(sec, "Show", false);
        var vid = f.Lib.UpsertVideo(ser, @"C:\m\Show\e01.mkv", 1, "mkv");
        f.Lib.SetResumePosition(vid, 30);
        f.Lib.SetDuration(vid, 120);
        // no chapters seeded

        await f.Vm.LoadAsync();

        f.Vm.ContinueWatching.Count.ShouldBe(1);
        var card = f.Vm.ContinueWatching[0];
        card.ChapterLabel.ShouldBeNull();
        card.HasChapter.ShouldBeFalse();
    }

    [Fact]
    public void EnqueueVideoCommand_adds_episode_to_queue_by_video_id()
    {
        var f = NewFx(); using var _d = f.Db;
        var src = f.Lib.UpsertSource(@"C:\m", "M");
        var sec = f.Lib.UpsertSection(src, "S");
        var ser = f.Lib.UpsertSeries(sec, "Show", false);
        var vid = f.Lib.UpsertVideo(ser, @"C:\m\Show\e01.mkv", 1, "mkv");

        f.Vm.EnqueueVideoCommand.Execute(vid);

        f.PlayQueue.HasQueue.ShouldBeTrue();
        f.PlayQueue.Items.Count.ShouldBe(1);
        f.PlayQueue.Items[0].Episode.VideoId.ShouldBe(vid);
    }
}
