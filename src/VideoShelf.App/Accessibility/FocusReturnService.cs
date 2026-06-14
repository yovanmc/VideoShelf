using System.Windows;

namespace VideoShelf.App.Accessibility;

public sealed class FocusReturnService : IFocusReturnService
{
    private IInputElement? _captured;

    public void Capture(IInputElement? element) => _captured = element;

    public IInputElement? TakeForRestore()
    {
        var el = _captured;
        _captured = null;
        return el;
    }
}
