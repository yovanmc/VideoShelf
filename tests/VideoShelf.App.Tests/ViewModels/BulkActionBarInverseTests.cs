// tests/VideoShelf.App.Tests/ViewModels/BulkActionBarInverseTests.cs
// B3 Step 6: round-trip test for the extracted bulk inverse methods.
// Uses AppTempDb + real repos — the established pattern in BulkActionBarViewModelTests.cs.

using System.Collections.Generic;
using Shouldly;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Storage;
using Xunit;

namespace VideoShelf.App.Tests.ViewModels;

public sealed class BulkActionBarInverseTests
{
    private sealed record Context(
        AppTempDb Temp,
        WatchRepository Watch,
        CurationRepository Curation,
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

        var srcId = lib.UpsertSource(@"C:\V", "V");
        var sectionId = lib.UpsertSection(srcId, "Creator A");
        var seriesId = lib.UpsertSeries(sectionId, "Series A", false);
        var v1 = lib.UpsertVideo(seriesId, @"C:\V\Creator A\e01.mp4", 1, ".mp4");
        var v2 = lib.UpsertVideo(seriesId, @"C:\V\Creator A\e02.mp4", 2, ".mp4");

        var vm = new BulkActionBarViewModel(watch, tags, curation, playlists, queue, lib);
        vm.SetVideoIds(new[] { v1, v2 });

        return new Context(temp, watch, curation, vm, v1, v2);
    }

    [Fact]
    public void MarkWatched_then_MarkUnwatchedIds_round_trips_watch_state()
    {
        var ctx = Build();
        var ids = new List<long> { ctx.VideoId1, ctx.VideoId2 };

        // Mark watched
        ctx.Vm.MarkWatchedCommand.Execute(null);
        ctx.Watch.IsWatched(ctx.VideoId1).ShouldBeTrue();
        ctx.Watch.IsWatched(ctx.VideoId2).ShouldBeTrue();

        // Inverse: mark unwatched
        ctx.Vm.MarkUnwatchedIds(ids);
        ctx.Watch.IsWatched(ctx.VideoId1).ShouldBeFalse();
        ctx.Watch.IsWatched(ctx.VideoId2).ShouldBeFalse();
    }

    [Fact]
    public void MarkUnwatched_then_MarkWatchedIds_round_trips_watch_state()
    {
        var ctx = Build();
        var ids = new List<long> { ctx.VideoId1, ctx.VideoId2 };

        // Pre-mark watched
        ctx.Watch.SetWatched(ctx.VideoId1, true);
        ctx.Watch.SetWatched(ctx.VideoId2, true);

        // Mark unwatched via command
        ctx.Vm.MarkUnwatchedCommand.Execute(null);
        ctx.Watch.IsWatched(ctx.VideoId1).ShouldBeFalse();
        ctx.Watch.IsWatched(ctx.VideoId2).ShouldBeFalse();

        // Inverse: mark watched
        ctx.Vm.MarkWatchedIds(ids);
        ctx.Watch.IsWatched(ctx.VideoId1).ShouldBeTrue();
        ctx.Watch.IsWatched(ctx.VideoId2).ShouldBeTrue();
    }

    [Fact]
    public void AddFavorite_then_RemoveFavoriteIds_round_trips_favorite_state()
    {
        var ctx = Build();
        var ids = new List<long> { ctx.VideoId1, ctx.VideoId2 };

        ctx.Vm.AddFavoriteCommand.Execute(null);
        ctx.Curation.IsFavorite(ctx.VideoId1).ShouldBeTrue();

        ctx.Vm.RemoveFavoriteIds(ids);
        ctx.Curation.IsFavorite(ctx.VideoId1).ShouldBeFalse();
    }

    [Fact]
    public void AddToWatchlist_then_RemoveFromWatchlistIds_round_trips_watchlist_state()
    {
        var ctx = Build();
        var ids = new List<long> { ctx.VideoId1, ctx.VideoId2 };

        ctx.Vm.AddToWatchlistCommand.Execute(null);
        ctx.Curation.InWatchlist(ctx.VideoId1).ShouldBeTrue();

        ctx.Vm.RemoveFromWatchlistIds(ids);
        ctx.Curation.InWatchlist(ctx.VideoId1).ShouldBeFalse();
    }
}
