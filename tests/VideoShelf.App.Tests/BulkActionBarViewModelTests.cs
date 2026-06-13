using System;
using System.Linq;
using Shouldly;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;
using Xunit;

namespace VideoShelf.App.Tests;

/// <summary>
/// B4 — BulkActionBarViewModel tests: each action mutates exactly the
/// selected ids; mark-watched clears resume + writes one watch_events row
/// per id; tags/favorite/watchlist/playlist all land.
/// Uses real repos over an in-memory (temp) DB.
/// </summary>
public sealed class BulkActionBarViewModelTests
{
    // ── Helper: seed DB + build the VM ───────────────────────────────────────

    private sealed record Context(
        AppTempDb Temp,
        LibraryRepository Library,
        WatchRepository Watch,
        TagRepository Tags,
        CurationRepository Curation,
        PlaylistRepository Playlists,
        PlayQueueViewModel Queue,
        BulkActionBarViewModel Vm,
        long VideoId1,
        long VideoId2);

    private static Context Build()
    {
        var temp = new AppTempDb();
        var lib = new LibraryRepository(temp.Db);
        var watch = new WatchRepository(temp.Db);
        var tags = new TagRepository(temp.Db);
        var curation = new CurationRepository(temp.Db);
        var playlists = new PlaylistRepository(temp.Db);
        var settings = new SettingsRepository(temp.Db);
        var queue = new PlayQueueViewModel(lib, settings);

        // Seed two videos in the same section.
        var srcId = lib.UpsertSource(@"C:\V", "V");
        var sectionId = lib.UpsertSection(srcId, "Creator A");
        var seriesId = lib.UpsertSeries(sectionId, "Series A", false);
        var v1 = lib.UpsertVideo(seriesId, @"C:\V\Creator A\e01.mp4", 1, ".mp4");
        var v2 = lib.UpsertVideo(seriesId, @"C:\V\Creator A\e02.mp4", 2, ".mp4");

        var vm = new BulkActionBarViewModel(watch, tags, curation, playlists, queue, lib);
        vm.SetVideoIds(new[] { v1, v2 });

        return new Context(temp, lib, watch, tags, curation, playlists, queue, vm, v1, v2);
    }

    private static int WatchEventCount(AppTempDb temp, long videoId)
    {
        using var conn = temp.Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM watch_events WHERE video_id = $id";
        cmd.Parameters.AddWithValue("$id", videoId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static (string? ResumePos, string? ResumeAt) GetResume(AppTempDb temp, long videoId)
    {
        using var conn = temp.Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT resume_position, resume_updated_at FROM videos WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", videoId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return (null, null);
        var pos = r.IsDBNull(0) ? null : r.GetString(0);
        var at = r.IsDBNull(1) ? null : r.GetString(1);
        return (pos, at);
    }

    private static void SetResumePosition(AppTempDb temp, long videoId)
    {
        using var conn = temp.Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE videos SET resume_position='00:01:00', resume_updated_at='2026-01-01T00:00:00Z' WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", videoId);
        cmd.ExecuteNonQuery();
    }

    // ── SetVideoIds ───────────────────────────────────────────────────────────

    [Fact]
    public void SetVideoIds_updates_count_and_label()
    {
        using var ctx = Build().Temp;
        var vm = Build();
        try
        {
            vm.Vm.SetVideoIds(new long[] { 1, 2, 3 });
            vm.Vm.SelectedCount.ShouldBe(3);
            vm.Vm.SelectedCountLabel.ShouldBe("3 selected");

            vm.Vm.SetVideoIds(new long[] { 99 });
            vm.Vm.SelectedCount.ShouldBe(1);
            vm.Vm.SelectedCountLabel.ShouldBe("1 selected");
        }
        finally { vm.Temp.Dispose(); }
    }

    // ── MarkWatched ───────────────────────────────────────────────────────────

    [Fact]
    public void MarkWatched_sets_watched_for_exactly_selected_ids()
    {
        var ctx = Build();
        using var _ = ctx.Temp;

        ctx.Vm.MarkWatchedCommand.Execute(null);

        ctx.Watch.IsWatched(ctx.VideoId1).ShouldBeTrue();
        ctx.Watch.IsWatched(ctx.VideoId2).ShouldBeTrue();
    }

    [Fact]
    public void MarkWatched_inserts_one_watch_event_per_video()
    {
        var ctx = Build();
        using var _ = ctx.Temp;

        ctx.Vm.MarkWatchedCommand.Execute(null);

        WatchEventCount(ctx.Temp, ctx.VideoId1).ShouldBe(1);
        WatchEventCount(ctx.Temp, ctx.VideoId2).ShouldBe(1);
    }

    [Fact]
    public void MarkWatched_clears_resume_position_for_each_video()
    {
        var ctx = Build();
        using var _ = ctx.Temp;

        // Seed resume position for both videos.
        SetResumePosition(ctx.Temp, ctx.VideoId1);
        SetResumePosition(ctx.Temp, ctx.VideoId2);

        ctx.Vm.MarkWatchedCommand.Execute(null);

        var (pos1, at1) = GetResume(ctx.Temp, ctx.VideoId1);
        var (pos2, at2) = GetResume(ctx.Temp, ctx.VideoId2);
        pos1.ShouldBeNull();
        at1.ShouldBeNull();
        pos2.ShouldBeNull();
        at2.ShouldBeNull();
    }

    // ── MarkUnwatched ─────────────────────────────────────────────────────────

    [Fact]
    public void MarkUnwatched_clears_watched_for_exactly_selected_ids()
    {
        var ctx = Build();
        using var _ = ctx.Temp;

        // First mark watched so there's something to unwatch.
        ctx.Vm.MarkWatchedCommand.Execute(null);
        ctx.Vm.MarkUnwatchedCommand.Execute(null);

        ctx.Watch.IsWatched(ctx.VideoId1).ShouldBeFalse();
        ctx.Watch.IsWatched(ctx.VideoId2).ShouldBeFalse();
    }

    [Fact]
    public void MarkUnwatched_does_not_add_watch_events()
    {
        var ctx = Build();
        using var _ = ctx.Temp;

        // No events before.
        ctx.Vm.MarkUnwatchedCommand.Execute(null);

        WatchEventCount(ctx.Temp, ctx.VideoId1).ShouldBe(0);
        WatchEventCount(ctx.Temp, ctx.VideoId2).ShouldBe(0);
    }

    // ── ApplyTag ─────────────────────────────────────────────────────────────

    [Fact]
    public void ApplyTag_adds_tag_to_all_selected_videos()
    {
        var ctx = Build();
        using var _ = ctx.Temp;

        ctx.Vm.PendingTag = "action";
        ctx.Vm.ApplyTagCommand.Execute(null);

        ctx.Tags.GetVideoTags(ctx.VideoId1).ShouldContain("action");
        ctx.Tags.GetVideoTags(ctx.VideoId2).ShouldContain("action");
    }

    [Fact]
    public void ApplyTag_normalizes_tag_and_clears_pending()
    {
        var ctx = Build();
        using var _ = ctx.Temp;

        ctx.Vm.PendingTag = "  Action Movie  ";
        ctx.Vm.ApplyTagCommand.Execute(null);

        // TagRepository.Normalize lowercases and collapses spaces.
        ctx.Tags.GetVideoTags(ctx.VideoId1).ShouldContain("action movie");
        ctx.Vm.PendingTag.ShouldBe(string.Empty);
    }

    [Fact]
    public void ApplyTag_with_empty_pending_does_nothing()
    {
        var ctx = Build();
        using var _ = ctx.Temp;

        ctx.Vm.PendingTag = "   ";
        ctx.Vm.ApplyTagCommand.Execute(null);

        ctx.Tags.GetVideoTags(ctx.VideoId1).ShouldBeEmpty();
    }

    // ── AddFavorite / RemoveFavorite ──────────────────────────────────────────

    [Fact]
    public void AddFavorite_marks_all_selected_videos_as_favorite()
    {
        var ctx = Build();
        using var _ = ctx.Temp;

        ctx.Vm.AddFavoriteCommand.Execute(null);

        ctx.Curation.IsFavorite(ctx.VideoId1).ShouldBeTrue();
        ctx.Curation.IsFavorite(ctx.VideoId2).ShouldBeTrue();
    }

    [Fact]
    public void RemoveFavorite_clears_favorite_for_all_selected_videos()
    {
        var ctx = Build();
        using var _ = ctx.Temp;

        ctx.Vm.AddFavoriteCommand.Execute(null);
        ctx.Vm.RemoveFavoriteCommand.Execute(null);

        ctx.Curation.IsFavorite(ctx.VideoId1).ShouldBeFalse();
        ctx.Curation.IsFavorite(ctx.VideoId2).ShouldBeFalse();
    }

    // ── AddToWatchlist ────────────────────────────────────────────────────────

    [Fact]
    public void AddToWatchlist_sets_watchlist_for_all_selected_videos()
    {
        var ctx = Build();
        using var _ = ctx.Temp;

        ctx.Vm.AddToWatchlistCommand.Execute(null);

        ctx.Curation.InWatchlist(ctx.VideoId1).ShouldBeTrue();
        ctx.Curation.InWatchlist(ctx.VideoId2).ShouldBeTrue();
    }

    // ── AddToPlaylist ─────────────────────────────────────────────────────────

    [Fact]
    public void AddToPlaylist_adds_all_selected_videos_to_the_playlist()
    {
        var ctx = Build();
        using var _ = ctx.Temp;

        var playlistId = ctx.Playlists.Create("My List", DateTimeOffset.UtcNow);
        var playlist = ctx.Playlists.GetAll().Single(p => p.Id == playlistId);

        ctx.Vm.AddToPlaylistCommand.Execute(playlist);

        var items = ctx.Playlists.GetItems(playlistId);
        items.Select(e => e.VideoId).ShouldContain(ctx.VideoId1);
        items.Select(e => e.VideoId).ShouldContain(ctx.VideoId2);
    }

    [Fact]
    public void AddToPlaylist_with_null_playlist_is_a_noop()
    {
        var ctx = Build();
        using var _ = ctx.Temp;

        // Should not throw.
        ctx.Vm.AddToPlaylistCommand.Execute(null);
    }

    // ── AddToQueue ────────────────────────────────────────────────────────────

    [Fact]
    public void AddToQueue_enqueues_each_resolved_episode()
    {
        var ctx = Build();
        using var _ = ctx.Temp;

        ctx.Vm.AddToQueueCommand.Execute(null);

        ctx.Queue.Items.Count.ShouldBe(2);
        ctx.Queue.IsExplicitQueue.ShouldBeTrue();
        ctx.Queue.Items.Select(i => i.Episode.VideoId).ShouldContain(ctx.VideoId1);
        ctx.Queue.Items.Select(i => i.Episode.VideoId).ShouldContain(ctx.VideoId2);
    }

    // ── Completed event ───────────────────────────────────────────────────────

    [Fact]
    public void Every_action_raises_Completed_once()
    {
        var ctx = Build();
        using var _ = ctx.Temp;

        var playlistId = ctx.Playlists.Create("P", DateTimeOffset.UtcNow);
        var playlist = ctx.Playlists.GetAll().Single(p => p.Id == playlistId);

        var completedCount = 0;
        ctx.Vm.Completed += (_, _) => completedCount++;

        ctx.Vm.MarkWatchedCommand.Execute(null);     // +1
        ctx.Vm.MarkUnwatchedCommand.Execute(null);   // +1
        ctx.Vm.PendingTag = "drama";
        ctx.Vm.ApplyTagCommand.Execute(null);         // +1
        ctx.Vm.AddFavoriteCommand.Execute(null);      // +1
        ctx.Vm.RemoveFavoriteCommand.Execute(null);   // +1
        ctx.Vm.AddToWatchlistCommand.Execute(null);   // +1
        ctx.Vm.RemoveFromWatchlistCommand.Execute(null); // +1
        ctx.Vm.AddToPlaylistCommand.Execute(playlist); // +1
        ctx.Vm.AddToQueueCommand.Execute(null);       // +1

        completedCount.ShouldBe(9);
    }
}
