using System;
using System.Collections.ObjectModel;
using Shouldly;
using VideoShelf.App.Motion;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.Tests;

/// <summary>Minimal IToastService fake that counts Show() calls and captures the last undo callback.</summary>
file sealed class CountingToastService : IToastService
{
    public ObservableCollection<ToastViewModel> Toasts { get; } = new();
    public int ShowCount { get; private set; }
    public Action? LastUndo { get; private set; }

    public void Show(string message, Action? undo = null, ToastKind kind = ToastKind.Info)
    {
        ShowCount++;
        LastUndo = undo;
    }

    public void Dismiss(ToastViewModel toast) { }
}

public class EpisodeViewModelTests
{
    private static (WatchRepository watch, long videoId) Seed(AppTempDb temp)
    {
        var lib = new LibraryRepository(temp.Db);
        var seriesId = lib.UpsertSeries(lib.UpsertSection(lib.UpsertSource(@"C:\V", "V"), "S"), "Base", false);
        var videoId = lib.UpsertVideo(seriesId, @"C:\V\S\a.mp4", 1, ".mp4");
        return (new WatchRepository(temp.Db), videoId);
    }

    [Fact]
    public void ToggleWatched_flips_flag_and_persists()
    {
        using var temp = new AppTempDb();
        var (watch, videoId) = Seed(temp);
        var view = new EpisodeView(videoId, 1, @"C:\V\S\a.mp4", 1, "Base", Watched: false, Missing: false);
        var vm = new EpisodeViewModel(view, watch);

        vm.ToggleWatchedCommand.Execute(null);

        vm.Watched.ShouldBeTrue();
        watch.IsWatched(videoId).ShouldBeTrue();

        vm.ToggleWatchedCommand.Execute(null);
        vm.Watched.ShouldBeFalse();
        watch.IsWatched(videoId).ShouldBeFalse();
    }

    [Fact]
    public void Missing_episode_exposes_flag_for_dimming()
    {
        using var temp = new AppTempDb();
        var (watch, videoId) = Seed(temp);
        var view = new EpisodeView(videoId, 1, @"C:\V\S\a.mp4", 1, "Base", Watched: false, Missing: true);

        var vm = new EpisodeViewModel(view, watch);

        vm.IsMissing.ShouldBeTrue();
        vm.Title.ShouldBe("Base");
    }

    [Fact]
    public void ToggleFavorite_undo_callback_does_NOT_show_a_second_toast()
    {
        using var temp = new AppTempDb();
        var (watch, videoId) = Seed(temp);
        var curation = new CurationRepository(temp.Db);
        var toasts = new CountingToastService();
        var view = new EpisodeView(videoId, 1, @"C:\V\S\a.mp4", 1, "Base", Watched: false, Missing: false);
        var vm = new EpisodeViewModel(view, watch, curation: curation, toasts: toasts);

        // One toggle → one toast.
        vm.ToggleFavoriteCommand.Execute(null);
        toasts.ShowCount.ShouldBe(1);

        // Invoke the undo callback directly; it must not call Show() again.
        toasts.LastUndo.ShouldNotBeNull();
        toasts.LastUndo!();
        toasts.ShowCount.ShouldBe(1, "undo should not show another toast");

        // State was restored.
        vm.IsFavorite.ShouldBeFalse();
        curation.IsFavorite(videoId).ShouldBeFalse();
    }

    // ── C2: progress fraction + runtime label ──────────────────────────────

    [Fact]
    public void ProgressFraction_is_zero_when_duration_unknown()
    {
        using var temp = new AppTempDb();
        var (watch, videoId) = Seed(temp);
        var view = new EpisodeView(videoId, 1, @"C:\V\S\a.mp4", 1, "Base",
            Watched: false, Missing: false, Duration: null, ResumePosition: 0);
        var vm = new EpisodeViewModel(view, watch);

        vm.ProgressFraction.ShouldBe(0.0);
        vm.HasProgress.ShouldBeFalse();
        vm.RuntimeLabel.ShouldBeNull();
    }

    [Fact]
    public void ProgressFraction_computed_from_duration_and_resume()
    {
        using var temp = new AppTempDb();
        var (watch, videoId) = Seed(temp);
        var view = new EpisodeView(videoId, 1, @"C:\V\S\a.mp4", 1, "Base",
            Watched: false, Missing: false, Duration: 3600.0, ResumePosition: 900.0);
        var vm = new EpisodeViewModel(view, watch);

        vm.ProgressFraction.ShouldBe(0.25, tolerance: 0.001);
        vm.HasProgress.ShouldBeTrue();
    }

    [Fact]
    public void HasProgress_false_when_fully_watched()
    {
        using var temp = new AppTempDb();
        var (watch, videoId) = Seed(temp);
        var view = new EpisodeView(videoId, 1, @"C:\V\S\a.mp4", 1, "Base",
            Watched: true, Missing: false, Duration: 1800.0, ResumePosition: 1800.0);
        var vm = new EpisodeViewModel(view, watch);

        // fraction = 1.0 → HasProgress = false (not in-progress, fully watched)
        vm.ProgressFraction.ShouldBe(1.0, tolerance: 0.001);
        vm.HasProgress.ShouldBeFalse();
    }

    [Theory]
    [InlineData(3661.0, "1:01")]   // 1 hour 1 minute → h:mm
    [InlineData(3600.0, "1:00")]   // exactly 1 hour
    [InlineData(125.0,  "2:05")]   // 2 min 5 sec → m:ss
    [InlineData(59.0,   "0:59")]   // under 1 minute
    public void RuntimeLabel_formats_correctly(double seconds, string expected)
    {
        using var temp = new AppTempDb();
        var (watch, videoId) = Seed(temp);
        var view = new EpisodeView(videoId, 1, @"C:\V\S\a.mp4", 1, "Base",
            Watched: false, Missing: false, Duration: seconds, ResumePosition: 0);
        var vm = new EpisodeViewModel(view, watch);

        vm.RuntimeLabel.ShouldBe(expected);
    }
}
