// tests/VideoShelf.App.Tests/RenameNavigationTests.cs
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using VideoShelf.App.Services;
using VideoShelf.App.ViewModels;
using Xunit;

namespace VideoShelf.App.Tests;

public class RenameNavigationTests
{
    [Fact]
    public void DiContainer_ResolvesMainViewModel_WithRenameToolWired()
    {
        // Mirrors how the app composes services; if AddVideoShelf needs a real DB path it should
        // already be covered by existing App.Tests DI tests — follow their pattern if this differs.
        var services = new ServiceCollection().AddVideoShelf().BuildServiceProvider();
        var main = services.GetRequiredService<MainViewModel>();
        main.RenameTool.ShouldNotBeNull();
        main.CurrentView.ShouldBe(AppView.Home);
    }
}
