using System;
using Shouldly;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;
using Xunit;

namespace VideoShelf.App.Tests;

public class PlaylistsViewModelTests
{
    private static (PlaylistRepository repo, LibraryRepository lib, long videoId) Seed(AppTempDb temp)
    {
        var lib = new LibraryRepository(temp.Db);
        var srcId = lib.UpsertSource(@"C:\V", "V");
        var secId = lib.UpsertSection(srcId, "S");
        var seriesId = lib.UpsertSeries(secId, "Base", false);
        var vid = lib.UpsertVideo(seriesId, @"C:\V\S\a.mp4", 1, ".mp4");
        var repo = new PlaylistRepository(temp.Db);
        return (repo, lib, vid);
    }

    private static (PlaylistRepository repo, LibraryRepository lib, long v1, long v2, long v3) SeedThree(AppTempDb temp)
    {
        var lib = new LibraryRepository(temp.Db);
        var srcId = lib.UpsertSource(@"C:\V", "V");
        var secId = lib.UpsertSection(srcId, "S");
        var seriesId = lib.UpsertSeries(secId, "Base", false);
        var v1 = lib.UpsertVideo(seriesId, @"C:\V\S\a.mp4", 1, ".mp4");
        var v2 = lib.UpsertVideo(seriesId, @"C:\V\S\b.mp4", 2, ".mp4");
        var v3 = lib.UpsertVideo(seriesId, @"C:\V\S\c.mp4", 3, ".mp4");
        var repo = new PlaylistRepository(temp.Db);
        return (repo, lib, v1, v2, v3);
    }

    // ── CreatePlaylist ────────────────────────────────────────────────────────

    [Fact]
    public void CreatePlaylist_adds_to_Playlists_and_selects_it()
    {
        using var temp = new AppTempDb();
        var (repo, _, _) = Seed(temp);
        var settings = new SettingsRepository(temp.Db);
        var lib = new LibraryRepository(temp.Db);
        var playQueue = new PlayQueueViewModel(lib, settings);
        var vm = new PlaylistsViewModel(repo, playQueue);

        vm.CreatePlaylistCommand.Execute(null);

        vm.Playlists.Count.ShouldBe(1);
        vm.Selected.ShouldNotBeNull();
        vm.Selected!.Name.ShouldBe("New playlist");
    }

    // ── OpenPlaylist ──────────────────────────────────────────────────────────

    [Fact]
    public void OpenPlaylist_sets_Selected_and_fills_Items()
    {
        using var temp = new AppTempDb();
        var (repo, _, videoId) = Seed(temp);
        var settings = new SettingsRepository(temp.Db);
        var lib = new LibraryRepository(temp.Db);
        var playQueue = new PlayQueueViewModel(lib, settings);
        var vm = new PlaylistsViewModel(repo, playQueue);

        var pid = repo.Create("P", DateTimeOffset.UtcNow);
        repo.AddItem(pid, videoId);
        vm.Load();

        vm.OpenPlaylistCommand.Execute(vm.Playlists[0]);

        vm.Selected.ShouldNotBeNull();
        vm.Items.Count.ShouldBe(1);
        vm.Items[0].VideoId.ShouldBe(videoId);
    }

    // ── Reorder ───────────────────────────────────────────────────────────────

    [Fact]
    public void MoveItemUp_moves_item_to_earlier_position()
    {
        using var temp = new AppTempDb();
        var (repo, _, v1, v2, v3) = SeedThree(temp);
        var settings = new SettingsRepository(temp.Db);
        var lib = new LibraryRepository(temp.Db);
        var playQueue = new PlayQueueViewModel(lib, settings);
        var vm = new PlaylistsViewModel(repo, playQueue);

        var pid = repo.Create("P", DateTimeOffset.UtcNow);
        repo.AddItem(pid, v1);
        repo.AddItem(pid, v2);
        repo.AddItem(pid, v3);
        vm.Load();
        vm.OpenPlaylistCommand.Execute(vm.Playlists[0]);

        // Move v2 (index 1) up to index 0
        var row = vm.Items[1]; // v2
        vm.MoveItemUpCommand.Execute(row);

        vm.Items[0].VideoId.ShouldBe(v2);
        vm.Items[1].VideoId.ShouldBe(v1);
        vm.Items[2].VideoId.ShouldBe(v3);
    }

    [Fact]
    public void MoveItemDown_moves_item_to_later_position()
    {
        using var temp = new AppTempDb();
        var (repo, _, v1, v2, v3) = SeedThree(temp);
        var settings = new SettingsRepository(temp.Db);
        var lib = new LibraryRepository(temp.Db);
        var playQueue = new PlayQueueViewModel(lib, settings);
        var vm = new PlaylistsViewModel(repo, playQueue);

        var pid = repo.Create("P", DateTimeOffset.UtcNow);
        repo.AddItem(pid, v1);
        repo.AddItem(pid, v2);
        repo.AddItem(pid, v3);
        vm.Load();
        vm.OpenPlaylistCommand.Execute(vm.Playlists[0]);

        // Move v1 (index 0) down to index 1
        var row = vm.Items[0]; // v1
        vm.MoveItemDownCommand.Execute(row);

        vm.Items[0].VideoId.ShouldBe(v2);
        vm.Items[1].VideoId.ShouldBe(v1);
        vm.Items[2].VideoId.ShouldBe(v3);
    }

    // ── RemoveItem ────────────────────────────────────────────────────────────

    [Fact]
    public void RemoveItem_removes_from_Items_and_persists()
    {
        using var temp = new AppTempDb();
        var (repo, _, videoId) = Seed(temp);
        var settings = new SettingsRepository(temp.Db);
        var lib = new LibraryRepository(temp.Db);
        var playQueue = new PlayQueueViewModel(lib, settings);
        var vm = new PlaylistsViewModel(repo, playQueue);

        var pid = repo.Create("P", DateTimeOffset.UtcNow);
        repo.AddItem(pid, videoId);
        vm.Load();
        vm.OpenPlaylistCommand.Execute(vm.Playlists[0]);
        vm.Items.Count.ShouldBe(1);

        vm.RemoveItemCommand.Execute(vm.Items[0]);

        vm.Items.ShouldBeEmpty();
        repo.GetItems(pid).ShouldBeEmpty();
    }

    // ── PlayAll ───────────────────────────────────────────────────────────────

    [Fact]
    public void PlayAll_raises_PlayRequested_on_queue_with_items()
    {
        using var temp = new AppTempDb();
        var (repo, _, videoId) = Seed(temp);
        var settings = new SettingsRepository(temp.Db);
        var lib = new LibraryRepository(temp.Db);
        var playQueue = new PlayQueueViewModel(lib, settings);
        var vm = new PlaylistsViewModel(repo, playQueue);

        var pid = repo.Create("P", DateTimeOffset.UtcNow);
        repo.AddItem(pid, videoId);
        vm.Load();
        vm.OpenPlaylistCommand.Execute(vm.Playlists[0]);

        EpisodeView? played = null;
        playQueue.PlayRequested += (_, ep) => played = ep;

        vm.PlayAllCommand.Execute(null);

        played.ShouldNotBeNull();
        played!.VideoId.ShouldBe(videoId);
    }

    [Fact]
    public void PlayAll_does_nothing_when_no_playlist_selected()
    {
        using var temp = new AppTempDb();
        var (repo, _, _) = Seed(temp);
        var settings = new SettingsRepository(temp.Db);
        var lib = new LibraryRepository(temp.Db);
        var playQueue = new PlayQueueViewModel(lib, settings);
        var vm = new PlaylistsViewModel(repo, playQueue);

        EpisodeView? played = null;
        playQueue.PlayRequested += (_, ep) => played = ep;

        vm.PlayAllCommand.Execute(null); // no selected playlist

        played.ShouldBeNull();
    }
}
