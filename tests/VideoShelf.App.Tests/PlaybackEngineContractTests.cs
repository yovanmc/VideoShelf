using Shouldly;
using VideoShelf.App.Services;

namespace VideoShelf.App.Tests;

public class PlaybackEngineContractTests
{
    [Fact]
    public void TrackOption_carries_id_and_label()
    {
        var t = new TrackOption(2, "English");

        t.Id.ShouldBe(2);
        t.Label.ShouldBe("English");
    }

    [Fact]
    public void SubtitlesOff_is_the_well_known_disabled_id()
    {
        // libVLC uses -1 for "no subtitle track".
        TrackOption.SubtitlesOffId.ShouldBe(-1);
    }

}
