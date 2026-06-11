using Shouldly;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;

namespace VideoShelf.Core.Tests.Storage;

public class SettingsRepositoryTests
{
    [Fact]
    public void AutoAdvance_defaults_to_true_when_unset()
    {
        using var temp = new TempDb();
        var settings = new SettingsRepository(temp.Db);

        settings.GetAutoAdvanceEpisodes().ShouldBeTrue();
    }

    [Fact]
    public void AutoAdvance_roundtrips_false()
    {
        using var temp = new TempDb();
        var settings = new SettingsRepository(temp.Db);

        settings.SetAutoAdvanceEpisodes(false);

        settings.GetAutoAdvanceEpisodes().ShouldBeFalse();
    }

    [Fact]
    public void AutoAdvance_roundtrips_back_to_true()
    {
        using var temp = new TempDb();
        var settings = new SettingsRepository(temp.Db);

        settings.SetAutoAdvanceEpisodes(false);
        settings.SetAutoAdvanceEpisodes(true);

        settings.GetAutoAdvanceEpisodes().ShouldBeTrue();
    }

    [Fact]
    public void GetString_returns_fallback_when_key_missing()
    {
        using var temp = new TempDb();
        var settings = new SettingsRepository(temp.Db);

        settings.GetString("nope", "fallback").ShouldBe("fallback");
    }

    [Fact]
    public void SetString_then_GetString_roundtrips()
    {
        using var temp = new TempDb();
        var settings = new SettingsRepository(temp.Db);

        settings.SetString("k", "v");

        settings.GetString("k", "fallback").ShouldBe("v");
    }
}
