// tests/VideoShelf.App.Tests/Motion/ScrollMemoryTests.cs
using VideoShelf.App.Motion;
using VideoShelf.App.ViewModels;   // AppView
using Xunit; using Shouldly;

public class ScrollMemoryTests
{
    [Fact]
    public void Remembers_and_returns_offset_per_view()
    {
        var store = new ScrollOffsetStore();
        store.Save(AppView.Browse, 250);
        store.TryGet(AppView.Browse, out var y).ShouldBeTrue();
        y.ShouldBe(250);
    }

    [Fact]
    public void Unknown_view_returns_false()
    {
        var store = new ScrollOffsetStore();
        store.TryGet(AppView.Home, out _).ShouldBeFalse();
    }
}
