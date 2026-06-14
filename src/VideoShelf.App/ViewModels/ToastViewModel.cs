using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using VideoShelf.App.Motion;

namespace VideoShelf.App.ViewModels;

public sealed partial class ToastViewModel : ObservableObject
{
    public string Message { get; }
    public ToastKind Kind { get; }
    public ICommand? UndoCommand { get; }

    /// <summary>True when an Undo action is available; drives the Undo button's Visibility binding.</summary>
    public bool HasUndo => UndoCommand != null;

    public ToastViewModel(string message, ToastKind kind, ICommand? undoCommand)
    {
        Message = message;
        Kind = kind;
        UndoCommand = undoCommand;
    }
}
