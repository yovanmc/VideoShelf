using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using VideoShelf.App.Services;
using VideoShelf.App.ViewModels;

namespace VideoShelf.App.Tests;

public class HostBuildsTests
{
    [Fact]
    public void AddVideoShelf_resolves_main_viewmodel()
    {
        var provider = new ServiceCollection().AddVideoShelf().BuildServiceProvider();

        var vm = provider.GetRequiredService<MainViewModel>();

        vm.Title.ShouldBe("VideoShelf");
    }

    [Fact]
    public void AddVideoShelf_resolves_player_viewmodel()
    {
        var provider = new ServiceCollection().AddVideoShelf().BuildServiceProvider();

        var player = provider.GetRequiredService<VideoShelf.App.ViewModels.PlayerViewModel>();

        player.ShouldNotBeNull();
    }

    [Fact]
    public void AddVideoShelf_resolves_settings_repository()
    {
        var provider = new ServiceCollection().AddVideoShelf().BuildServiceProvider();

        provider.GetRequiredService<VideoShelf.Core.Storage.SettingsRepository>().ShouldNotBeNull();
    }
}
