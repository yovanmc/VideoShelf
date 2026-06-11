using Shouldly;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.Tests;

public class SettingsViewModelTests
{
    [Fact]
    public void AutoAdvance_defaults_true_from_repository()
    {
        using var temp = new AppTempDb();
        var vm = new SettingsViewModel(new SettingsRepository(temp.Db));

        vm.AutoAdvanceEpisodes.ShouldBeTrue();
    }

    [Fact]
    public void Setting_AutoAdvance_false_persists()
    {
        using var temp = new AppTempDb();
        var settings = new SettingsRepository(temp.Db);
        var vm = new SettingsViewModel(settings);

        vm.AutoAdvanceEpisodes = false;

        settings.GetAutoAdvanceEpisodes().ShouldBeFalse();
        new SettingsViewModel(settings).AutoAdvanceEpisodes.ShouldBeFalse();
    }
}
