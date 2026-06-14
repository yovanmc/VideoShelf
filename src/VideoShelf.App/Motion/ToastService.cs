// src/VideoShelf.App/Motion/ToastService.cs
using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using VideoShelf.App.ViewModels;

namespace VideoShelf.App.Motion;

public sealed class ToastService : IToastService
{
    private static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(5);
    private readonly Action<TimeSpan, Action> _scheduleDismiss;

    public ObservableCollection<ToastViewModel> Toasts { get; } = new();

    /// <param name="scheduleDismiss">Seam: schedules <paramref name="scheduleDismiss"/>'s
    /// action after the delay. Production passes a DispatcherTimer-backed scheduler;
    /// tests capture and fire it synchronously.</param>
    public ToastService(Action<TimeSpan, Action> scheduleDismiss)
        => _scheduleDismiss = scheduleDismiss;

    public void Show(string message, Action? undo = null, ToastKind kind = ToastKind.Info)
    {
        ToastViewModel toast = null!;
        RelayCommand? undoCmd = undo is null ? null : new RelayCommand(() =>
        {
            undo();
            Dismiss(toast);
        });
        toast = new ToastViewModel(message, kind, undoCmd);
        Toasts.Add(toast);
        _scheduleDismiss(DefaultDuration, () => Dismiss(toast));
    }

    public void Dismiss(ToastViewModel toast)
    {
        if (Toasts.Contains(toast)) Toasts.Remove(toast);
    }
}
