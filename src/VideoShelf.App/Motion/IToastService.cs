// src/VideoShelf.App/Motion/IToastService.cs
using System;
namespace VideoShelf.App.Motion;
public interface IToastService
{
    System.Collections.ObjectModel.ObservableCollection<VideoShelf.App.ViewModels.ToastViewModel> Toasts { get; }
    void Show(string message, Action? undo = null, ToastKind kind = ToastKind.Info);
    void Dismiss(VideoShelf.App.ViewModels.ToastViewModel toast);
}
