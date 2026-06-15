using System;
using System.Windows;
using System.Windows.Controls;

namespace VideoShelf.App.Views;

public partial class BulkActionBar : UserControl
{
    /// <summary>Raised when the user clicks the Dismiss/Clear (✕) button.
    /// The host window should clear the selection and hide the bar.</summary>
    public event EventHandler? ClearRequested;

    /// <summary>
    /// Raised when the user clicks the "Rename…" button.
    /// Scoped to the creator-grid Browse page.
    /// </summary>
    public event EventHandler? RenameRequested;

    public BulkActionBar()
    {
        InitializeComponent();
        ClearButton.Click += (_, _) => ClearRequested?.Invoke(this, EventArgs.Empty);
        RenameButton.Click += (_, _) => RenameRequested?.Invoke(this, EventArgs.Empty);
    }
}
