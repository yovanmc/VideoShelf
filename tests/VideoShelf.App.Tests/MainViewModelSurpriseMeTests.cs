using Shouldly;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.Tests;

public class MainViewModelSurpriseMeTests
{
    [Fact]
    public void SurpriseMe_opens_player_when_an_unwatched_episode_exists()
    {
        // Factory already seeds one unwatched, non-missing video.
        var vm = MainViewModelTestFactory.Create(out var ctx);
        using var _ = ctx.Db;

        vm.SurpriseMeCommand.Execute(null);

        vm.IsPlayerVisible.ShouldBeTrue();
    }

    [Fact]
    public void SurpriseMe_is_noop_when_all_episodes_are_watched()
    {
        var vm = MainViewModelTestFactory.Create(out var ctx);
        using var _ = ctx.Db;

        // Mark every video in the DB as watched.
        var lib = new LibraryRepository(ctx.Db.Db);
        var watch = new WatchRepository(ctx.Db.Db);
        foreach (var source in lib.GetSources())
        {
            foreach (var section in lib.GetSections(source.Id))
            {
                foreach (var series in lib.GetSeriesForSection(section.Id))
                {
                    foreach (var video in lib.GetVideosForSeries(series.Id))
                    {
                        watch.SetWatched(video.Id, true);
                    }
                }
            }
        }

        vm.SurpriseMeCommand.Execute(null);

        // No player should open — nothing to surprise with.
        vm.IsPlayerVisible.ShouldBeFalse();
    }
}
