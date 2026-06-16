using System;
using System.IO;
using Shouldly;
using VideoShelf.App.Services;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;
using Xunit;

namespace VideoShelf.App.Tests.FailPaths;

/// <summary>
/// C1 — when the playback engine raises <see cref="VideoShelf.App.Services.IPlaybackEngine.EncounteredError"/>,
/// the player must surface a visible, user-facing error state (never a silent black screen) and clear the
/// playing flag. Pins the M3-era engine-error guard wired in PlayerViewModel.OnEngineError.
/// </summary>
public class PlayerErrorSurfaceTests
{
    private static PlayerViewModel Vm(AppTempDb temp, FakePlaybackEngine engine, string realFile, out EpisodeView ep)
    {
        var lib = new LibraryRepository(temp.Db);
        var seriesId = lib.UpsertSeries(lib.UpsertSection(lib.UpsertSource(@"C:\V", "V"), "S"), "Base", false);
        var videoId = lib.UpsertVideo(seriesId, realFile, 1, ".mp4");
        ep = new EpisodeView(videoId, seriesId, realFile, 1, "Base", Watched: false, Missing: false);
        return new PlayerViewModel(engine, lib, new WatchRepository(temp.Db),
            new SettingsRepository(temp.Db), new ResumePolicy(), new FakeSubtitleFilePicker());
    }

    [Fact]
    public void Engine_error_after_load_surfaces_visible_error_and_stops_playing()
    {
        using var temp = new AppTempDb();
        // A real file so Open() proceeds past the missing-file guard into engine.Load/Play.
        var realFile = Path.Combine(Path.GetTempPath(), "vshelf_c1_" + Guid.NewGuid().ToString("N") + ".mp4");
        File.WriteAllBytes(realFile, new byte[] { 0x00, 0x01, 0x02, 0x03 });
        try
        {
            var engine = new FakePlaybackEngine();
            var vm = Vm(temp, engine, realFile, out var ep);

            vm.Open(ep);
            engine.LoadedPath.ShouldBe(realFile); // sanity: we are past the missing-file guard
            vm.IsPlaying.ShouldBeTrue();
            vm.HasError.ShouldBeFalse();

            // libVLC hit an unrecoverable error for the loaded media.
            engine.RaiseError();

            // The error is SURFACED, not silent.
            vm.HasError.ShouldBeTrue();
            vm.PlaybackError.ShouldNotBeNullOrEmpty();
            // ...and the loading/playing flag clears so the UI doesn't sit on a frozen "playing" state.
            vm.IsPlaying.ShouldBeFalse();
        }
        finally
        {
            try { File.Delete(realFile); } catch { }
        }
    }

    [Fact]
    public void Has_error_tracks_playback_error_so_the_bound_panel_is_not_permanently_collapsed()
    {
        using var temp = new AppTempDb();
        var engine = new FakePlaybackEngine();
        var lib = new LibraryRepository(temp.Db);
        var seriesId = lib.UpsertSeries(lib.UpsertSection(lib.UpsertSource(@"C:\V", "V"), "S"), "Base", false);
        var videoId = lib.UpsertVideo(seriesId, @"C:\V\S\x.mp4", 1, ".mp4");
        var ep = new EpisodeView(videoId, seriesId, @"C:\V\S\x.mp4", 1, "Base", Watched: false, Missing: true);
        var vm = new PlayerViewModel(engine, lib, new WatchRepository(temp.Db),
            new SettingsRepository(temp.Db), new ResumePolicy(), new FakeSubtitleFilePicker());

        // No error initially → panel hidden.
        vm.HasError.ShouldBeFalse();

        // Missing-file path also drives the same visible-error state the panel binds to.
        vm.Open(ep);
        vm.HasError.ShouldBeTrue();
        vm.PlaybackError.ShouldNotBeNullOrEmpty();
    }
}
