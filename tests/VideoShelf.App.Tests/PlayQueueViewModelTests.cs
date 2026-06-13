using Shouldly;
using VideoShelf.App.ViewModels;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;
using Xunit;

namespace VideoShelf.App.Tests;

public sealed class PlayQueueViewModelTests
{
    private static EpisodeView Ep(long id, long series, int no, string title) =>
        new(id, series, $@"C:\V\{title}.mp4", no, title, false, false);

    private static (PlayQueueViewModel q, AppTempDb db, LibraryRepository lib) New()
    {
        var db = new AppTempDb();
        var lib = new LibraryRepository(db.Db);
        var settings = new SettingsRepository(db.Db);
        return (new PlayQueueViewModel(lib, settings), db, lib);
    }

    [Fact]
    public void PlayAll_sets_first_as_current_and_requests_play()
    {
        var (q, db, _) = New();
        using var _d = db;
        EpisodeView? played = null;
        q.PlayRequested += (_, e) => played = e;
        q.PlayAll(new[] { Ep(1,1,1,"A"), Ep(2,1,2,"B") });
        q.HasQueue.ShouldBeTrue();
        q.CurrentIndex.ShouldBe(0);
        played!.Title.ShouldBe("A");
        q.Items[0].IsNowPlaying.ShouldBeTrue();
    }

    [Fact]
    public void GetNextAfterEnd_advances_then_clears_on_exhaustion()
    {
        var (q, db, _) = New();
        using var _d = db;
        q.PlayAll(new[] { Ep(1,1,1,"A"), Ep(2,1,2,"B") });
        var next = q.GetNextAfterEnd(q.Items[0].Episode);
        next!.Title.ShouldBe("B");
        q.CurrentIndex.ShouldBe(1);
        var after = q.GetNextAfterEnd(q.Items[0].Episode); // last item finished
        after.ShouldBeNull();
        q.HasQueue.ShouldBeFalse();
        q.Items.Count.ShouldBe(0);
    }

    [Fact]
    public void StartSingle_falls_back_to_series_auto_advance_when_enabled()
    {
        var (q, db, lib) = New();
        using var _d = db;
        var src = lib.UpsertSource(@"C:\V", "V");
        var sec = lib.UpsertSection(src, "Creator");
        var ser = lib.UpsertSeries(sec, "Show", isStandalone: false);
        lib.UpsertVideo(ser, @"C:\V\Creator\Show 1.mp4", 1, ".mp4");
        lib.UpsertVideo(ser, @"C:\V\Creator\Show 2.mp4", 2, ".mp4");
        var first = lib.GetEpisodes(ser)[0];

        q.StartSingle(first);
        q.HasQueue.ShouldBeFalse();           // single play => no queue UI
        var next = q.GetNextAfterEnd(first);
        next.ShouldNotBeNull();
        next!.EpisodeNo.ShouldBe(2);
    }

    [Fact]
    public void Enqueue_then_end_of_single_plays_queue()
    {
        var (q, db, _) = New();
        using var _d = db;
        q.StartSingle(Ep(1,1,1,"X"));
        q.Enqueue(Ep(2,2,1,"Y"));
        q.HasQueue.ShouldBeTrue();            // enqueue promotes to explicit
        var next = q.GetNextAfterEnd(q.Items[0].Episode);
        next!.Title.ShouldBe("Y");
    }

    [Fact]
    public void PlayNext_inserts_after_current()
    {
        var (q, db, _) = New();
        using var _d = db;
        q.PlayAll(new[] { Ep(1,1,1,"A"), Ep(2,1,2,"B") });
        q.PlayNext(Ep(3,3,1,"C"));
        q.Items[1].Title.ShouldBe("C");
        q.Items[2].Title.ShouldBe("B");
    }

    [Fact]
    public void Remove_before_current_keeps_now_playing()
    {
        var (q, db, _) = New();
        using var _d = db;
        q.PlayAll(new[] { Ep(1,1,1,"A"), Ep(2,1,2,"B"), Ep(3,1,3,"C") });
        q.GetNextAfterEnd(q.Items[0].Episode); // now playing index 1 ("B")
        q.RemoveItemCommand.Execute(q.Items[0]); // remove "A"
        q.Items[q.CurrentIndex].Title.ShouldBe("B");
    }

    [Fact]
    public void MoveDown_keeps_now_playing_pointer()
    {
        var (q, db, _) = New();
        using var _d = db;
        q.PlayAll(new[] { Ep(1,1,1,"A"), Ep(2,1,2,"B"), Ep(3,1,3,"C") });
        var a = q.Items[0]; // current
        q.MoveDownCommand.Execute(a);
        q.Items[1].Title.ShouldBe("A");
        q.Items[q.CurrentIndex].Title.ShouldBe("A");
    }

    [Fact]
    public void JumpTo_requests_play_and_sets_current()
    {
        var (q, db, _) = New();
        using var _d = db;
        EpisodeView? played = null;
        q.PlayRequested += (_, e) => played = e;
        q.PlayAll(new[] { Ep(1,1,1,"A"), Ep(2,1,2,"B"), Ep(3,1,3,"C") });
        q.JumpToCommand.Execute(q.Items[2]);
        q.CurrentIndex.ShouldBe(2);
        played!.Title.ShouldBe("C");
    }
}
