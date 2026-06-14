// tests/VideoShelf.App.Tests/Motion/ToastServiceTests.cs
using System;
using System.Collections.Generic;
using VideoShelf.App.Motion;
using Xunit;
using Shouldly;

public class ToastServiceTests
{
    // Capture the scheduled dismiss so we can fire it manually.
    private static (ToastService svc, List<Action> pending) Make()
    {
        var pending = new List<Action>();
        var svc = new ToastService((delay, act) => pending.Add(act));
        return (svc, pending);
    }

    [Fact]
    public void Show_adds_a_toast()
    {
        var (svc, _) = Make();
        svc.Show("Marked watched");
        svc.Toasts.Count.ShouldBe(1);
        svc.Toasts[0].Message.ShouldBe("Marked watched");
    }

    [Fact]
    public void Auto_dismiss_removes_the_toast()
    {
        var (svc, pending) = Make();
        svc.Show("Hi");
        pending[0].Invoke();          // simulate the timer firing
        svc.Toasts.ShouldBeEmpty();
    }

    [Fact]
    public void Undo_invokes_callback_and_dismisses()
    {
        var (svc, _) = Make();
        var undone = false;
        svc.Show("Removed source", undo: () => undone = true);
        svc.Toasts[0].UndoCommand!.Execute(null);
        undone.ShouldBeTrue();
        svc.Toasts.ShouldBeEmpty();
    }

    [Fact]
    public void Toast_without_undo_has_no_undo_command()
    {
        var (svc, _) = Make();
        svc.Show("Scan complete");
        svc.Toasts[0].UndoCommand.ShouldBeNull();
    }
}
