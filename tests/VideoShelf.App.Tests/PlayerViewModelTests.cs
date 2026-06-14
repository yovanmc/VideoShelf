using Shouldly;
using VideoShelf.App.Services;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.Tests;

public class PlayerViewModelTests
{
    private static (LibraryRepository lib, WatchRepository watch, SettingsRepository settings, EpisodeView ep)
        Seed(AppTempDb temp, double? resume = null, int episodeNo = 1)
    {
        var lib = new LibraryRepository(temp.Db);
        var sectionId = lib.UpsertSection(lib.UpsertSource(@"C:\V", "V"), "S");
        var seriesId = lib.UpsertSeries(sectionId, "Base", false);
        // GetTempFileName creates a real empty file so the missing-file guard passes.
        var path = System.IO.Path.GetTempFileName();
        var videoId = lib.UpsertVideo(seriesId, path, episodeNo, ".mp4");
        if (resume is { } r) lib.SetResumePosition(videoId, r);
        var ep = new EpisodeView(videoId, seriesId, path, episodeNo, "Base", Watched: false, Missing: false);
        return (lib, new WatchRepository(temp.Db), new SettingsRepository(temp.Db), ep);
    }

    private static PlayerViewModel NewVm(AppTempDb temp, FakePlaybackEngine engine,
        LibraryRepository lib, WatchRepository watch, SettingsRepository settings)
        => new(engine, lib, watch, settings, new ResumePolicy(), new FakeSubtitleFilePicker());

    [Fact]
    public void Open_loads_path_into_engine_and_plays()
    {
        using var temp = new AppTempDb();
        var (lib, watch, settings, ep) = Seed(temp);
        var engine = new FakePlaybackEngine();
        var vm = NewVm(temp, engine, lib, watch, settings);

        vm.Open(ep);

        engine.LoadedPath.ShouldBe(ep.FilePath);
        engine.IsPlaying.ShouldBeTrue();
    }

    [Fact]
    public void Open_with_resumable_position_sets_ResumeOffer()
    {
        using var temp = new AppTempDb();
        var (lib, watch, settings, ep) = Seed(temp, resume: 50.0);
        var engine = new FakePlaybackEngine();
        var vm = NewVm(temp, engine, lib, watch, settings);

        vm.Open(ep);
        engine.RaiseLength(100.0); // length needed for the resume threshold check

        vm.CanResume.ShouldBeTrue();
        vm.ResumePositionSeconds.ShouldBe(50.0);
    }

    [Fact]
    public void ResumeCommand_seeks_to_saved_position()
    {
        using var temp = new AppTempDb();
        var (lib, watch, settings, ep) = Seed(temp, resume: 50.0);
        var engine = new FakePlaybackEngine();
        var vm = NewVm(temp, engine, lib, watch, settings);

        vm.Open(ep);
        engine.RaiseLength(100.0);
        vm.ResumeCommand.Execute(null);

        engine.Seeks.ShouldContain(50.0);
        vm.CanResume.ShouldBeFalse();
    }

    [Fact]
    public void Position_tick_saves_resume_after_interval()
    {
        using var temp = new AppTempDb();
        var (lib, watch, settings, ep) = Seed(temp);
        var engine = new FakePlaybackEngine();
        var vm = NewVm(temp, engine, lib, watch, settings);
        vm.Open(ep);
        engine.RaiseLength(100.0);

        engine.RaisePosition(3.0);   // below 5s interval since 0 → no save
        lib.GetResumePosition(ep.VideoId).ShouldBeNull();

        engine.RaisePosition(6.0);   // crosses the 5s interval → save
        lib.GetResumePosition(ep.VideoId).ShouldBe(6.0);
    }

    [Fact]
    public void TogglePlayPause_flushes_resume_position()
    {
        using var temp = new AppTempDb();
        var (lib, watch, settings, ep) = Seed(temp);
        var engine = new FakePlaybackEngine();
        var vm = NewVm(temp, engine, lib, watch, settings);
        vm.Open(ep);
        engine.RaiseLength(100.0);
        engine.RaisePosition(2.0); // below interval, not yet saved

        vm.TogglePlayPauseCommand.Execute(null); // pause → flush

        engine.IsPlaying.ShouldBeFalse();
        lib.GetResumePosition(ep.VideoId).ShouldBe(2.0);
    }

    [Fact]
    public void Scrubbing_freezes_ScrubPosition_from_position_updates()
    {
        using var temp = new AppTempDb();
        var (lib, watch, settings, ep) = Seed(temp);
        var engine = new FakePlaybackEngine();
        var vm = NewVm(temp, engine, lib, watch, settings);
        vm.Open(ep);

        vm.BeginScrub();
        vm.ScrubPosition = 42;
        engine.RaisePosition(5);

        vm.ScrubPosition.ShouldBe(42);
        vm.IsScrubbing.ShouldBeTrue();
    }

    [Fact]
    public void Not_scrubbing_ScrubPosition_tracks_playback()
    {
        using var temp = new AppTempDb();
        var (lib, watch, settings, ep) = Seed(temp);
        var engine = new FakePlaybackEngine();
        var vm = NewVm(temp, engine, lib, watch, settings);
        vm.Open(ep);

        engine.RaiseLength(100);
        engine.RaisePosition(30);

        vm.ScrubPosition.ShouldBe(30);
    }

    [Fact]
    public void CommitScrub_seeks_engine_to_scrub_position_and_ends_gesture()
    {
        using var temp = new AppTempDb();
        var (lib, watch, settings, ep) = Seed(temp);
        var engine = new FakePlaybackEngine();
        var vm = NewVm(temp, engine, lib, watch, settings);
        vm.Open(ep);

        vm.BeginScrub();
        vm.ScrubPosition = 55;
        vm.CommitScrub();

        engine.Seeks[^1].ShouldBe(55);
        vm.IsScrubbing.ShouldBeFalse();
        vm.SeekPreviewPath.ShouldBeNull();
    }

    [Fact]
    public async Task UpdateScrubPreviewAsync_passes_position_to_engine_and_sets_path()
    {
        using var temp = new AppTempDb();
        var (lib, watch, settings, ep) = Seed(temp);
        var engine = new FakePlaybackEngine();
        var vm = NewVm(temp, engine, lib, watch, settings);
        vm.Open(ep);

        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());
        System.IO.Directory.CreateDirectory(dir);
        vm.SeekPreviewDirectory = dir;

        await vm.UpdateScrubPreviewAsync(33);

        engine.LastPreviewSeconds.ShouldBe(33);
        vm.SeekPreviewPath.ShouldNotBeNull();
    }

    [Fact]
    public async Task UpdateScrubPreviewAsync_returns_null_path_when_engine_fails()
    {
        using var temp = new AppTempDb();
        var (lib, watch, settings, ep) = Seed(temp);
        var engine = new FakePlaybackEngine();
        var vm = NewVm(temp, engine, lib, watch, settings);
        vm.Open(ep);

        engine.SnapshotShouldFail = true;
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());
        System.IO.Directory.CreateDirectory(dir);
        vm.SeekPreviewDirectory = dir;

        await vm.UpdateScrubPreviewAsync(20);

        vm.SeekPreviewPath.ShouldBeNull();
    }

    // ===== B-VM: SkipBack10 / SkipForward30 / ToggleMute ===================

    [Fact]
    public void SkipBack10_seeks_back_10_seconds()
    {
        using var temp = new AppTempDb();
        var (lib, watch, settings, ep) = Seed(temp);
        var engine = new FakePlaybackEngine();
        var vm = NewVm(temp, engine, lib, watch, settings);
        vm.Open(ep);
        engine.RaiseLength(100.0);
        engine.RaisePosition(50.0);

        vm.SkipBack10Command.Execute(null);

        engine.Seeks[^1].ShouldBe(40.0);
    }

    [Fact]
    public void SkipBack10_clamps_to_zero_when_near_start()
    {
        using var temp = new AppTempDb();
        var (lib, watch, settings, ep) = Seed(temp);
        var engine = new FakePlaybackEngine();
        var vm = NewVm(temp, engine, lib, watch, settings);
        vm.Open(ep);
        engine.RaiseLength(100.0);
        engine.RaisePosition(5.0);

        vm.SkipBack10Command.Execute(null);

        engine.Seeks[^1].ShouldBe(0.0);
    }

    [Fact]
    public void SkipForward30_seeks_forward_30_seconds()
    {
        using var temp = new AppTempDb();
        var (lib, watch, settings, ep) = Seed(temp);
        var engine = new FakePlaybackEngine();
        var vm = NewVm(temp, engine, lib, watch, settings);
        vm.Open(ep);
        engine.RaiseLength(200.0);
        engine.RaisePosition(50.0);

        vm.SkipForward30Command.Execute(null);

        engine.Seeks[^1].ShouldBe(80.0);
    }

    [Fact]
    public void SkipForward30_clamps_to_length_when_near_end()
    {
        using var temp = new AppTempDb();
        var (lib, watch, settings, ep) = Seed(temp);
        var engine = new FakePlaybackEngine();
        var vm = NewVm(temp, engine, lib, watch, settings);
        vm.Open(ep);
        engine.RaiseLength(100.0);
        engine.RaisePosition(85.0);

        vm.SkipForward30Command.Execute(null);

        engine.Seeks[^1].ShouldBe(100.0);
    }

    [Fact]
    public void SkipForward30_does_not_clamp_when_length_unknown()
    {
        using var temp = new AppTempDb();
        var (lib, watch, settings, ep) = Seed(temp);
        var engine = new FakePlaybackEngine();
        var vm = NewVm(temp, engine, lib, watch, settings);
        vm.Open(ep);
        // LengthSeconds stays 0 (length not raised)
        engine.RaisePosition(50.0);

        vm.SkipForward30Command.Execute(null);

        engine.Seeks[^1].ShouldBe(80.0);
    }

    [Fact]
    public void ToggleMute_sets_volume_to_zero_and_remembers_previous()
    {
        using var temp = new AppTempDb();
        var (lib, watch, settings, ep) = Seed(temp);
        var engine = new FakePlaybackEngine();
        var vm = NewVm(temp, engine, lib, watch, settings);
        vm.Volume = 80;

        vm.ToggleMuteCommand.Execute(null);

        vm.IsMuted.ShouldBeTrue();
        vm.Volume.ShouldBe(0);
    }

    [Fact]
    public void ToggleMute_restores_volume_on_unmute()
    {
        using var temp = new AppTempDb();
        var (lib, watch, settings, ep) = Seed(temp);
        var engine = new FakePlaybackEngine();
        var vm = NewVm(temp, engine, lib, watch, settings);
        vm.Volume = 75;

        vm.ToggleMuteCommand.Execute(null); // mute
        vm.ToggleMuteCommand.Execute(null); // unmute

        vm.IsMuted.ShouldBeFalse();
        vm.Volume.ShouldBe(75);
    }

    [Fact]
    public void ToggleMute_when_volume_already_zero_restores_to_100()
    {
        using var temp = new AppTempDb();
        var (lib, watch, settings, ep) = Seed(temp);
        var engine = new FakePlaybackEngine();
        var vm = NewVm(temp, engine, lib, watch, settings);
        vm.Volume = 0;

        vm.ToggleMuteCommand.Execute(null); // mute with volume 0 → saves 100
        vm.ToggleMuteCommand.Execute(null); // unmute → restores 100

        vm.IsMuted.ShouldBeFalse();
        vm.Volume.ShouldBe(100);
    }
}
