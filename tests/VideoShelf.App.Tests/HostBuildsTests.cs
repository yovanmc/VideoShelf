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

    [Fact]
    public void Host_resolves_discovery_services()
    {
        var provider = new ServiceCollection().AddVideoShelf().BuildServiceProvider();

        provider.GetService(typeof(VideoShelf.Core.Storage.TagRepository)).ShouldNotBeNull();
        provider.GetService(typeof(VideoShelf.Core.Discovery.DiscoveryRepository)).ShouldNotBeNull();
        provider.GetService(typeof(VideoShelf.App.ViewModels.Discovery.DiscoveryViewModel)).ShouldNotBeNull();
        provider.GetService(typeof(VideoShelf.App.ViewModels.SectionDetailViewModel)).ShouldNotBeNull();
    }
}
