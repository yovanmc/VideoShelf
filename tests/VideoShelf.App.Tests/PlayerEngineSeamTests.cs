using System.Linq;
using Shouldly;
using VideoShelf.App.Services;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.Tests;

/// <summary>
/// Group C unit tests: engine seam extensions (Rate/AspectRatio/Scale/VolumeNormalize/TracksChanged)
/// and the M13 ESAdded fix (TracksChanged → RefreshTracks in PlayerViewModel).
/// All tests run against FakePlaybackEngine — no real libVLC needed.
/// </summary>
public class PlayerEngineSeamTests
{
    // ── helpers ────────────────────────────────────────────────────────────────

    private static (PlayerViewModel vm, FakePlaybackEngine engine, EpisodeView ep) Make(AppTempDb temp)
    {
        var lib = new LibraryRepository(temp.Db);
        var sectionId = lib.UpsertSection(lib.UpsertSource(@"C:\V", "V"), "S");
        var seriesId = lib.UpsertSeries(sectionId, "Base", false);
        var path = System.IO.Path.GetTempFileName(); // real file so the missing-file guard passes
        var videoId = lib.UpsertVideo(seriesId, path, 1, ".mp4");
        var ep = new EpisodeView(videoId, seriesId, path, 1, "Base", Watched: false, Missing: false);
        var engine = new FakePlaybackEngine();
        var vm = new PlayerViewModel(engine, lib,
            new WatchRepository(temp.Db),
            new SettingsRepository(temp.Db),
            new ResumePolicy(),
            new FakeSubtitleFilePicker());
        return (vm, engine, ep);
    }

    // ── C1: ClampRate pure helper ─────────────────────────────────────────────

    [Fact]
    public void ClampRate_clamps_out_of_range_values()
    {
        LibVlcPlaybackEngine.ClampRate(0.25).ShouldBe(LibVlcPlaybackEngine.MinRate); // below min → min
        LibVlcPlaybackEngine.ClampRate(3.0).ShouldBe(LibVlcPlaybackEngine.MaxRate);  // above max → max
        LibVlcPlaybackEngine.ClampRate(1.0).ShouldBe(1.0);                           // in range → unchanged
        LibVlcPlaybackEngine.ClampRate(0.5).ShouldBe(LibVlcPlaybackEngine.MinRate);  // boundary min
        LibVlcPlaybackEngine.ClampRate(2.0).ShouldBe(LibVlcPlaybackEngine.MaxRate);  // boundary max
    }

    // ── C1 / C5: Rate round-trip ───────────────────────────────────────────────

    [Fact]
    public void Rate_defaults_to_1_on_fake()
    {
        var engine = new FakePlaybackEngine();

        engine.Rate.ShouldBe(1.0);
    }

    [Fact]
    public void Rate_stores_and_retrieves_value()
    {
        var engine = new FakePlaybackEngine();

        engine.Rate = 1.5;

        engine.Rate.ShouldBe(1.5);
    }

    [Fact]
    public void Rate_stores_boundary_values()
    {
        var engine = new FakePlaybackEngine();

        engine.Rate = 0.5;
        engine.Rate.ShouldBe(0.5);

        engine.Rate = 2.0;
        engine.Rate.ShouldBe(2.0);
    }

    // ── C1 / C5: AspectRatio round-trip ───────────────────────────────────────

    [Fact]
    public void AspectRatio_defaults_to_null()
    {
        var engine = new FakePlaybackEngine();

        engine.AspectRatio.ShouldBeNull();
    }

    [Fact]
    public void AspectRatio_stores_and_retrieves_value()
    {
        var engine = new FakePlaybackEngine();

        engine.AspectRatio = "16:9";

        engine.AspectRatio.ShouldBe("16:9");
    }

    [Fact]
    public void AspectRatio_can_be_cleared_to_null()
    {
        var engine = new FakePlaybackEngine();
        engine.AspectRatio = "4:3";

        engine.AspectRatio = null;

        engine.AspectRatio.ShouldBeNull();
    }

    // ── C1 / C5: Scale round-trip ─────────────────────────────────────────────

    [Fact]
    public void Scale_defaults_to_zero()
    {
        var engine = new FakePlaybackEngine();

        engine.Scale.ShouldBe(0f);
    }

    [Fact]
    public void Scale_stores_and_retrieves_value()
    {
        var engine = new FakePlaybackEngine();

        engine.Scale = 1.5f;

        engine.Scale.ShouldBe(1.5f);
    }

    // ── C1 / C5: VolumeNormalize round-trip ──────────────────────────────────

    [Fact]
    public void SupportsVolumeNormalize_is_true_on_fake()
    {
        var engine = new FakePlaybackEngine();

        engine.SupportsVolumeNormalize.ShouldBeTrue();
    }

    [Fact]
    public void VolumeNormalizeEnabled_defaults_to_false()
    {
        var engine = new FakePlaybackEngine();

        engine.VolumeNormalizeEnabled.ShouldBeFalse();
    }

    [Fact]
    public void VolumeNormalizeEnabled_round_trips()
    {
        var engine = new FakePlaybackEngine();

        engine.VolumeNormalizeEnabled = true;
        engine.VolumeNormalizeEnabled.ShouldBeTrue();

        engine.VolumeNormalizeEnabled = false;
        engine.VolumeNormalizeEnabled.ShouldBeFalse();
    }

    // ── C4 / M13: TracksChanged → VM RefreshTracks ───────────────────────────

    [Fact]
    public void TracksChanged_event_fires_via_RaiseTracksChanged_helper()
    {
        var engine = new FakePlaybackEngine();
        var fired = false;
        engine.TracksChanged += (_, _) => fired = true;

        engine.RaiseTracksChanged();

        fired.ShouldBeTrue();
    }

    [Fact]
    public void RaiseTracksChanged_after_Open_populates_AudioTracks_collection()
    {
        using var temp = new AppTempDb();
        var (vm, engine, ep) = Make(temp);
        engine.AudioTracks.Add(new TrackOption(0, "Japanese"));
        engine.AudioTracks.Add(new TrackOption(1, "English"));
        vm.Open(ep);

        // Simulate libVLC discovering audio tracks after load (ESAdded)
        engine.RaiseTracksChanged();

        vm.AudioTracks.Select(t => t.Label).ShouldBe(new[] { "Japanese", "English" });
        vm.HasMultipleAudioTracks.ShouldBeTrue();
    }

    [Fact]
    public void RaiseTracksChanged_after_Open_populates_SubtitleTracks_collection()
    {
        using var temp = new AppTempDb();
        var (vm, engine, ep) = Make(temp);
        engine.SubtitleTracks.Add(new TrackOption(TrackOption.SubtitlesOffId, "Off"));
        engine.SubtitleTracks.Add(new TrackOption(3, "English"));
        vm.Open(ep);

        engine.RaiseTracksChanged();

        vm.SubtitleTracks.Count.ShouldBe(2);
        vm.SubtitleTracks.First().Id.ShouldBe(TrackOption.SubtitlesOffId);
        vm.HasSubtitleTracks.ShouldBeTrue();
    }

    [Fact]
    public void TracksChanged_unsubscribed_does_not_fire_after_second_Open()
    {
        // Opening a new episode detaches and re-attaches the handler.
        // Assert that only the current open's tracks are reflected (no double-fire).
        using var temp = new AppTempDb();
        var (vm, engine, ep) = Make(temp);
        engine.AudioTracks.Add(new TrackOption(0, "Track A"));
        vm.Open(ep);

        // Second open (re-subscribe): clear tracks, add new ones
        engine.AudioTracks.Clear();
        engine.AudioTracks.Add(new TrackOption(0, "Track B"));
        vm.Open(ep); // re-subscribes

        engine.RaiseTracksChanged();

        vm.AudioTracks.Select(t => t.Label).ShouldBe(new[] { "Track B" });
    }

    [Fact]
    public void TracksChanged_before_Open_does_not_throw()
    {
        // Guard: TracksChanged raised before Open() is called should not crash.
        using var temp = new AppTempDb();
        var (vm, engine, _) = Make(temp);

        // No exception — engine.TracksChanged fires but VM has no subscribers yet
        var ex = Record.Exception(() => engine.RaiseTracksChanged());
        ex.ShouldBeNull();
    }
}
