using Shouldly;
using VideoShelf.App.Services;
using VideoShelf.App.Tests.TestSupport;

namespace VideoShelf.App.Tests;

public class FakePlaybackEngineTests
{
    [Fact]
    public void Load_then_Play_sets_playing()
    {
        var engine = new FakePlaybackEngine();
        engine.Load(@"C:\V\a.mp4");
        engine.Play();

        engine.IsPlaying.ShouldBeTrue();
        engine.LoadedPath.ShouldBe(@"C:\V\a.mp4");
    }

    [Fact]
    public void RaisePosition_fires_PositionChanged_and_updates_Position()
    {
        var engine = new FakePlaybackEngine();
        double seen = -1;
        engine.PositionChanged += (_, p) => seen = p;

        engine.RaisePosition(12.0);

        seen.ShouldBe(12.0);
        engine.Position.ShouldBe(12.0);
    }

    [Fact]
    public void RaiseEnded_fires_Ended()
    {
        var engine = new FakePlaybackEngine();
        var fired = false;
        engine.Ended += (_, _) => fired = true;

        engine.RaiseEnded();

        fired.ShouldBeTrue();
    }

    [Fact]
    public void SetSubtitleTrack_records_selection()
    {
        var engine = new FakePlaybackEngine();
        engine.SetSubtitleTrack(TrackOption.SubtitlesOffId);

        engine.GetCurrentSubtitleTrack().ShouldBe(TrackOption.SubtitlesOffId);
    }
}
