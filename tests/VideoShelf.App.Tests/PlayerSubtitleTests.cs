using System.Linq;
using Shouldly;
using VideoShelf.App.Services;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.Tests;

public class PlayerSubtitleTests
{
    private static (PlayerViewModel vm, FakePlaybackEngine engine, FakeSubtitleFilePicker picker, EpisodeView ep)
        Make(AppTempDb temp)
    {
        var lib = new LibraryRepository(temp.Db);
        var seriesId = lib.UpsertSeries(lib.UpsertSection(lib.UpsertSource(@"C:\V", "V"), "S"), "Base", false);
        // GetTempFileName creates a real file on disk so the missing-file guard passes.
        var path = System.IO.Path.GetTempFileName();
        var videoId = lib.UpsertVideo(seriesId, path, 1, ".mp4");
        var ep = new EpisodeView(videoId, seriesId, path, 1, "Base", Watched: false, Missing: false);
        var engine = new FakePlaybackEngine();
        var picker = new FakeSubtitleFilePicker();
        var vm = new PlayerViewModel(engine, lib, new WatchRepository(temp.Db),
            new SettingsRepository(temp.Db), new ResumePolicy(), picker);
        return (vm, engine, picker, ep);
    }

    [Fact]
    public void AddSubtitleFile_attaches_and_surfaces_track()
    {
        using var temp = new AppTempDb();
        var (vm, engine, picker, ep) = Make(temp);
        // Seed the mandatory "Off" entry so HasSubtitleTracks (Count > 1) becomes true
        // once the sidecar track is added.
        engine.SubtitleTracks.Add(new TrackOption(TrackOption.SubtitlesOffId, "Off"));
        vm.Open(ep);

        picker.NextResult = @"C:\m\movie.en.srt";
        vm.AddSubtitleFileCommand.Execute(null);

        engine.AddedSubtitles.ShouldContain(@"C:\m\movie.en.srt");
        vm.SubtitleTracks.ShouldContain(t => t.Label == "movie.en.srt");
        vm.HasSubtitleTracks.ShouldBeTrue();
        vm.SelectedSubtitleTrack.ShouldNotBeNull();
        vm.SelectedSubtitleTrack!.Label.ShouldBe("movie.en.srt");
    }

    [Fact]
    public void AddSubtitleFile_noop_when_picker_cancels()
    {
        using var temp = new AppTempDb();
        var (vm, engine, picker, ep) = Make(temp);
        vm.Open(ep);

        picker.NextResult = null;
        vm.AddSubtitleFileCommand.Execute(null);

        engine.AddedSubtitles.ShouldBeEmpty();
    }

    [Fact]
    public void CurrentFilePath_and_CanAddSubtitle_reflect_state_after_Open()
    {
        using var temp = new AppTempDb();
        var (vm, _, _, ep) = Make(temp);

        vm.CanAddSubtitle.ShouldBeFalse();
        vm.CurrentFilePath.ShouldBeNull();

        vm.Open(ep);

        vm.CurrentFilePath.ShouldBe(ep.FilePath);
        vm.CanAddSubtitle.ShouldBeTrue();
    }
}
