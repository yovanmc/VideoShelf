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
}
