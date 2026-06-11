using System.Linq;
using Shouldly;
using VideoShelf.App.Services;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.Tests;

public class PlayerTracksAndChaptersTests
{
    private static (PlayerViewModel vm, FakePlaybackEngine engine, EpisodeView ep) Make(AppTempDb temp)
    {
        var lib = new LibraryRepository(temp.Db);
        var seriesId = lib.UpsertSeries(lib.UpsertSection(lib.UpsertSource(@"C:\V", "V"), "S"), "Base", false);
        var path = @"C:\V\S\a.mp4";
        var videoId = lib.UpsertVideo(seriesId, path, 1, ".mp4");
        var ep = new EpisodeView(videoId, seriesId, path, 1, "Base", false, false);
        var engine = new FakePlaybackEngine();
        var vm = new PlayerViewModel(engine, lib, new WatchRepository(temp.Db),
            new SettingsRepository(temp.Db), new ResumePolicy());
        return (vm, engine, ep);
    }

    [Fact]
    public void RefreshTracks_populates_audio_and_subtitle_collections()
    {
        using var temp = new AppTempDb();
        var (vm, engine, ep) = Make(temp);
        engine.AudioTracks.Add(new TrackOption(0, "Japanese"));
        engine.AudioTracks.Add(new TrackOption(1, "English"));
        engine.SubtitleTracks.Add(new TrackOption(TrackOption.SubtitlesOffId, "Off"));
        engine.SubtitleTracks.Add(new TrackOption(3, "English"));
        vm.Open(ep);

        vm.RefreshTracks();

        vm.AudioTracks.Select(t => t.Label).ShouldBe(new[] { "Japanese", "English" });
        vm.SubtitleTracks.First().Id.ShouldBe(TrackOption.SubtitlesOffId);
        vm.HasMultipleAudioTracks.ShouldBeTrue();
    }

    [Fact]
    public void SelectingSubtitleTrack_applies_to_engine()
    {
        using var temp = new AppTempDb();
        var (vm, engine, ep) = Make(temp);
        engine.SubtitleTracks.Add(new TrackOption(TrackOption.SubtitlesOffId, "Off"));
        engine.SubtitleTracks.Add(new TrackOption(3, "English"));
        vm.Open(ep);
        vm.RefreshTracks();

        vm.SelectedSubtitleTrack = vm.SubtitleTracks.First(t => t.Id == 3);

        engine.GetCurrentSubtitleTrack().ShouldBe(3);
    }

    [Fact]
    public void SelectingAudioTrack_applies_to_engine()
    {
        using var temp = new AppTempDb();
        var (vm, engine, ep) = Make(temp);
        engine.AudioTracks.Add(new TrackOption(0, "Japanese"));
        engine.AudioTracks.Add(new TrackOption(1, "English"));
        vm.Open(ep);
        vm.RefreshTracks();

        vm.SelectedAudioTrack = vm.AudioTracks.First(t => t.Id == 1);

        engine.GetCurrentAudioTrack().ShouldBe(1);
    }

    [Fact]
    public void RefreshTracks_populates_chapters_and_HasChapters()
    {
        using var temp = new AppTempDb();
        var (vm, engine, ep) = Make(temp);
        engine.Chapters.Add(new ChapterOption(0, "Intro"));
        engine.Chapters.Add(new ChapterOption(1, "Part 1"));
        vm.Open(ep);

        vm.RefreshTracks();

        vm.Chapters.Count.ShouldBe(2);
        vm.HasChapters.ShouldBeTrue();
    }

    [Fact]
    public void No_chapters_means_HasChapters_false()
    {
        using var temp = new AppTempDb();
        var (vm, engine, ep) = Make(temp);
        vm.Open(ep);

        vm.RefreshTracks();

        vm.HasChapters.ShouldBeFalse();
    }

    [Fact]
    public void NextChapter_and_PreviousChapter_call_engine()
    {
        using var temp = new AppTempDb();
        var (vm, engine, ep) = Make(temp);
        engine.Chapters.Add(new ChapterOption(0, "Intro"));
        vm.Open(ep);
        vm.RefreshTracks();

        vm.NextChapterCommand.Execute(null);
        vm.PreviousChapterCommand.Execute(null);

        engine.NextChapterCalls.ShouldBe(1);
        engine.PreviousChapterCalls.ShouldBe(1);
    }

    [Fact]
    public void Volume_setter_forwards_to_engine()
    {
        using var temp = new AppTempDb();
        var (vm, engine, ep) = Make(temp);
        vm.Open(ep);

        vm.Volume = 40;

        engine.Volume.ShouldBe(40);
    }

    [Fact]
    public void ToggleFullscreen_flips_IsFullscreen()
    {
        using var temp = new AppTempDb();
        var (vm, _, ep) = Make(temp);
        vm.Open(ep);

        vm.ToggleFullscreenCommand.Execute(null);

        vm.IsFullscreen.ShouldBeTrue();
    }
}
