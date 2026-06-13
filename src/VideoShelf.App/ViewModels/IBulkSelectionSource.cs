using System;
using System.Collections.Generic;

namespace VideoShelf.App.ViewModels;

/// <summary>
/// Implemented by page VMs that support multi-select + bulk action.
/// <see cref="MainViewModel"/> observes the active source and drives the
/// <see cref="BulkActionBarViewModel"/> from whichever page is currently shown.
/// </summary>
public interface IBulkSelectionSource
{
    /// <summary>True when at least one item is selected.</summary>
    bool HasSelection { get; }

    /// <summary>Returns the video ids for the current selection.</summary>
    IReadOnlyList<long> GetSelectedVideoIds();

    /// <summary>Raised when the selection set changes (items added or removed).</summary>
    event EventHandler? SelectionChanged;

    /// <summary>Clears the current selection without exiting selection mode.</summary>
    void ClearSelection();

    /// <summary>Exits selection mode (also clears the selection).</summary>
    void ExitSelectionMode();
}
