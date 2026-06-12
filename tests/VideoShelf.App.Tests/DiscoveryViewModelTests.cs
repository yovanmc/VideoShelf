using Shouldly;
using VideoShelf.App.ViewModels.Discovery;
using VideoShelf.Core.Discovery;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;
using VideoShelf.App.Tests.TestSupport;
using Xunit;

namespace VideoShelf.App.Tests;

public sealed class DiscoveryViewModelTests
{
    private sealed record Fx(AppTempDb Db, LibraryRepository Lib, WatchRepository Watch,
        TagRepository Tags, DiscoveryRepository Disc, DiscoveryViewModel Vm);

    private static Fx NewFx()
    {
        var db = new AppTempDb();
        var lib = new LibraryRepository(db.Db);
        var watch = new WatchRepository(db.Db);
        var tags = new TagRepository(db.Db);
        var disc = new DiscoveryRepository(db.Db, lib, tags);
        var vm = new DiscoveryViewModel(disc, lib, tags);
        return new Fx(db, lib, watch, tags, disc, vm);
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
    public async Task Section_card_Open_raises_SectionOpenRequested()
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
        f.Vm.ForYou.ShouldNotBeEmpty();

        long? opened = null;
        f.Vm.SectionOpenRequested += (_, id) => opened = id;
        f.Vm.ForYou[0].OpenCommand.Execute(null);
        opened.ShouldBe(candidate);
    }

    [Fact]
    public async Task ToggleTag_recomputes_tag_results()
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
        f.Vm.TagResults.Select(r => r.SectionId).ShouldContain(s1);
    }
}
