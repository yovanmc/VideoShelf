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
        {
            // GetTempFileName creates a real empty file so the missing-file guard passes.
            var path = System.IO.Path.GetTempFileName();
            lib.UpsertVideo(seriesId, path, n, ".mp4");
        }
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
        => new(engine, lib, watch, settings, new ResumePolicy(), new FakeSubtitleFilePicker());

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
    public void Ended_raises_PlaybackEnded_with_the_finished_episode()
    {
        using var temp = new AppTempDb();
        var (lib, watch, settings, seriesId) = SeedSeries(temp, episodes: 2);
        var ep1 = Ep(lib, seriesId, 1);
        var engine = new FakePlaybackEngine();
        var vm = NewVm(engine, lib, watch, settings);
        EpisodeView? ended = null;
        vm.PlaybackEnded += (_, e) => ended = e;
        vm.Open(ep1);
        engine.RaiseLength(100.0);

        engine.RaiseEnded();

        ended.ShouldNotBeNull();
        ended!.EpisodeNo.ShouldBe(1);
    }

    [Fact]
    public void Ended_raises_PlaybackEnded_regardless_of_auto_advance_setting()
    {
        // Auto-advance is no longer the player's concern — PlaybackEnded is always raised so
        // the host (MainViewModel via PlayQueueViewModel) can decide what plays next.
        using var temp = new AppTempDb();
        var (lib, watch, settings, seriesId) = SeedSeries(temp, episodes: 2);
        settings.SetAutoAdvanceEpisodes(false);
        var ep1 = Ep(lib, seriesId, 1);
        var engine = new FakePlaybackEngine();
        var vm = NewVm(engine, lib, watch, settings);
        EpisodeView? ended = null;
        vm.PlaybackEnded += (_, e) => ended = e;
        vm.Open(ep1);
        engine.RaiseLength(100.0);

        engine.RaiseEnded();

        ended.ShouldNotBeNull();
        ended!.VideoId.ShouldBe(ep1.VideoId);
    }
}
