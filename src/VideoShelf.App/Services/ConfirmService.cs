using System.Windows;

namespace VideoShelf.App.Services;

/// <summary>
/// Abstraction over a yes/no confirmation prompt, so ViewModels can be unit-tested
/// without a real WPF dialog. Confirmed = true means "yes, proceed".
/// </summary>
public interface IConfirmService
{
    bool Confirm(string title, string message);
}

/// <summary>WPF MessageBox implementation — shows a real dialog on the UI thread.</summary>
public sealed class ConfirmService : IConfirmService
{
    public bool Confirm(string title, string message)
        => MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
}
