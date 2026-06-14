using System;
using System.Threading;
using System.Windows.Controls;
using VideoShelf.App.Accessibility;
using Xunit;

namespace VideoShelf.App.Tests.Accessibility;

/// <summary>
/// Runs a delegate on a dedicated STA thread so WPF objects (which require STA) can be
/// instantiated.  xUnit uses MTA pool threads by default.
/// </summary>
file static class Sta
{
    public static void Run(Action action)
    {
        Exception? ex = null;
        var t = new Thread(() =>
        {
            try { action(); }
            catch (Exception e) { ex = e; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (ex is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex).Throw();
    }
}

// UIElement / FrameworkElement / Control implement IInputElement naturally.
// Control is the minimal concrete type we can instantiate without a PresentationSource.
file sealed class TestElement : Control { }

public class FocusReturnServiceTests
{
    [Fact]
    public void Capture_then_TakeForRestore_returns_same_element()
    {
        Sta.Run(() =>
        {
            var svc = new FocusReturnService();
            var el = new TestElement();
            svc.Capture(el);
            Assert.Same(el, svc.TakeForRestore());
        });
    }

    [Fact]
    public void TakeForRestore_with_nothing_captured_returns_null()
    {
        var svc = new FocusReturnService();
        Assert.Null(svc.TakeForRestore());
    }

    [Fact]
    public void TakeForRestore_clears_the_capture()
    {
        Sta.Run(() =>
        {
            var svc = new FocusReturnService();
            var el = new TestElement();
            svc.Capture(el);
            svc.TakeForRestore();
            Assert.Null(svc.TakeForRestore());
        });
    }
}
