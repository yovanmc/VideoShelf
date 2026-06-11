using Shouldly;
using VideoShelf.App.Services;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.Tests;

public class PlayerEndOfMediaTests
{
    private static (LibraryRepository lib, WatchRepository watch, SettingsRepository settings, long seriesId)
        SeedSeries(AppTempDb temp, int episodes)
    {
        var lib = new LibraryRepository(temp.Db);
        var sectionId = lib.UpsertSection(lib.UpsertSource(@"C:\V", "V"), "S");
        var seriesId = lib.UpsertSeries(sectionId, "Base", isStandalone: episodes == 1 ? false : false);
        for (var n = 1; n <= episodes; n++)
            lib.UpsertVideo(seriesId, $@"C:\V\S\e{n}.mp4", n, ".mp4");
        return (lib, new WatchRepository(temp.Db), new SettingsRepository(temp.Db), seriesId);
    }

    private static EpisodeView Ep(LibraryRepository lib, long seriesId, int n)
    {
        foreach (var e in lib.GetEpisodes(seriesId))
            if (e.EpisodeNo == n) return e;
        throw new System.InvalidOperationException("episode not found");
    }

    private static PlayerViewModel NewVm(FakePlaybackEngine engine,
        LibraryRepository lib, WatchRepository watch, SettingsRepository settings)
        => new(engine, lib, watch, settings, new ResumePolicy());

    [Fact]
    public void Ended_marks_watched_and_clears_resume()
    {
        using var temp = new AppTempDb();
        var (lib, watch, settings, seriesId) = SeedSeries(temp, episodes: 1);
        var ep = Ep(lib, seriesId, 1);
        var engine = new FakePlaybackEngine();
        var vm = NewVm(engine, lib, watch, settings);
        vm.Open(ep);
        engine.RaiseLength(100.0);
        engine.RaisePosition(40.0); // some progress saved

        engine.RaiseEnded();

        watch.IsWatched(ep.VideoId).ShouldBeTrue();
        lib.GetResumePosition(ep.VideoId).ShouldBeNull();
    }

    [Fact]
    public void Ended_requests_next_episode_when_auto_advance_on()
    {
        using var temp = new AppTempDb();
        var (lib, watch, settings, seriesId) = SeedSeries(temp, episodes: 2);
        settings.SetAutoAdvanceEpisodes(true);
        var ep1 = Ep(lib, seriesId, 1);
        var engine = new FakePlaybackEngine();
        var vm = NewVm(engine, lib, watch, settings);
        EpisodeView? requested = null;
        vm.NextEpisodeRequested += (_, e) => requested = e;
        vm.Open(ep1);
        engine.RaiseLength(100.0);

        engine.RaiseEnded();

        requested.ShouldNotBeNull();
        requested!.EpisodeNo.ShouldBe(2);
    }

    [Fact]
    public void Ended_does_not_request_next_when_auto_advance_off()
    {
        using var temp = new AppTempDb();
        var (lib, watch, settings, seriesId) = SeedSeries(temp, episodes: 2);
        settings.SetAutoAdvanceEpisodes(false);
        var ep1 = Ep(lib, seriesId, 1);
        var engine = new FakePlaybackEngine();
        var vm = NewVm(engine, lib, watch, settings);
        var fired = false;
        vm.NextEpisodeRequested += (_, _) => fired = true;
        vm.Open(ep1);
        engine.RaiseLength(100.0);

        engine.RaiseEnded();

        fired.ShouldBeFalse();
    }

    [Fact]
    public void Ended_on_last_episode_does_not_request_next()
    {
        using var temp = new AppTempDb();
        var (lib, watch, settings, seriesId) = SeedSeries(temp, episodes: 2);
        settings.SetAutoAdvanceEpisodes(true);
        var ep2 = Ep(lib, seriesId, 2);
        var engine = new FakePlaybackEngine();
        var vm = NewVm(engine, lib, watch, settings);
        var fired = false;
        vm.NextEpisodeRequested += (_, _) => fired = true;
        vm.Open(ep2);
        engine.RaiseLength(100.0);

        engine.RaiseEnded();

        fired.ShouldBeFalse();
    }
}
