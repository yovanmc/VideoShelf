using System.Windows;

namespace VideoShelf.App.Accessibility;

public interface IFocusReturnService
{
    void Capture(IInputElement? element);
    IInputElement? TakeForRestore();
}
