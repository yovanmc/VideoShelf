using Shouldly;
using VideoShelf.App.Services;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.Tests;

public class MainViewModelPlaybackTests
{
    private sealed class NullThumbs : IThumbnailService
    {
        public System.Threading.Tasks.Task<string?> GetThumbnailPathAsync(string videoPath, System.Threading.CancellationToken ct)
            => System.Threading.Tasks.Task.FromResult<string?>(null);
    }

    private sealed class NullScan : IScanCoordinator
    {
        public bool IsBusy => false;
        public System.Threading.Tasks.Task ScanAllAsync(System.Threading.CancellationToken ct)
            => System.Threading.Tasks.Task.CompletedTask;
    }

    private static MainViewModel Make(AppTempDb temp, FakePlaybackEngine engine, out long videoId)
    {
        var lib = new LibraryRepository(temp.Db);
        var watch = new WatchRepository(temp.Db);
        var settings = new SettingsRepository(temp.Db);
        var seriesId = lib.UpsertSeries(lib.UpsertSection(lib.UpsertSource(@"C:\V", "V"), "S"), "Base", false);
        videoId = lib.UpsertVideo(seriesId, @"C:\V\S\a.mp4", 1, ".mp4");
        var thumbs = new NullThumbs();
        var library = new LibraryViewModel(lib, watch, thumbs);
        var sources = new SourcesViewModel(lib, new FakeFolderPicker());
        var player = new PlayerViewModel(engine, lib, watch, settings, new ResumePolicy());
        return new MainViewModel(sources, library, new NullScan(), player);
    }

    [Fact]
    public void Playing_an_episode_opens_the_player_and_shows_player_pane()
    {
        using var temp = new AppTempDb();
        var engine = new FakePlaybackEngine();
        var vm = Make(temp, engine, out var videoId);
        var ep = new EpisodeView(videoId, 1, @"C:\V\S\a.mp4", 1, "Base", false, false);

        vm.PlayEpisode(ep);

        vm.IsPlayerVisible.ShouldBeTrue();
        // missing path → PlaybackError set, engine not loaded; the routing itself is what we assert:
        vm.Player.Title.ShouldBe("Base");
    }

    [Fact]
    public void TogglePiP_flips_IsPictureInPicture()
    {
        using var temp = new AppTempDb();
        var engine = new FakePlaybackEngine();
        var vm = Make(temp, engine, out _);

        vm.TogglePictureInPictureCommand.Execute(null);

        vm.IsPictureInPicture.ShouldBeTrue();
    }

    [Fact]
    public void NextEpisodeRequested_from_player_reopens_via_PlayEpisode()
    {
        using var temp = new AppTempDb();
        var engine = new FakePlaybackEngine();
        var vm = Make(temp, engine, out _);
        var ep = new EpisodeView(1, 1, @"C:\V\S\a.mp4", 1, "Base", false, false);
        var next = new EpisodeView(2, 1, @"C:\V\S\b.mp4", 2, "Base 2", false, false);
        vm.PlayEpisode(ep);

        vm.Player.RaiseNextEpisodeForTest(next);

        vm.Player.Title.ShouldBe("Base 2");
    }
}
