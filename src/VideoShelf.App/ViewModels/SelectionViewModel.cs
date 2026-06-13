using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VideoShelf.App.ViewModels;

/// <summary>
/// Marker interface for card view-models that can be selected in a multi-select grid.
/// The hosting page VM subscribes to each card's PropertyChanged and calls
/// <see cref="SelectionViewModel{T}.OnItemSelectionChanged"/> — the card never holds
/// a back-reference to the selection container.
/// </summary>
public interface ISelectableCard
{
    bool IsSelected { get; set; }
}

/// <summary>
/// Generic, reusable selection state for a page that hosts a selectable card grid.
/// Tracks which cards are selected, whether selection mode is active, and exposes
/// commands used by the "Select / Select all / Clear" toolbar affordance.
/// </summary>
public partial class SelectionViewModel<T> : ObservableObject where T : ISelectableCard
{
    [ObservableProperty]
    private bool _isSelectionMode;

    partial void OnIsSelectionModeChanged(bool value)
    {
        // When selection mode is turned off (e.g. toggle button unchecked),
        // automatically clear any existing selection so state stays consistent.
        if (!value)
            ClearSelection();
    }

    public ObservableCollection<T> SelectedItems { get; } = new();

    public int SelectedCount => SelectedItems.Count;
    public bool HasSelection => SelectedItems.Count > 0;

    // -----------------------------------------------------------------
    // Called by the hosting VM each time a card's IsSelected property
    // changes (via PropertyChanged subscription — no back-ref in card).
    // -----------------------------------------------------------------
    public void OnItemSelectionChanged(T item)
    {
        if (item.IsSelected)
        {
            if (!SelectedItems.Contains(item))
                SelectedItems.Add(item);
        }
        else
        {
            SelectedItems.Remove(item);
        }
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelection));
    }

    // -----------------------------------------------------------------
    // Commands
    // -----------------------------------------------------------------

    [RelayCommand]
    private void EnterSelectionMode()
    {
        IsSelectionMode = true;
    }

    [RelayCommand]
    private void ExitSelectionMode()
    {
        IsSelectionMode = false;
        ClearSelection();
    }

    [RelayCommand]
    private void ClearSelection()
    {
        // De-select each item first so cards update their IsSelected state.
        foreach (var item in SelectedItems)
            item.IsSelected = false;
        SelectedItems.Clear();
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelection));
    }

    [RelayCommand]
    private void SelectAll(IEnumerable<T>? all)
    {
        if (all is null) return;
        foreach (var item in all)
            item.IsSelected = true;
        // SelectedItems is updated via OnItemSelectionChanged subscriptions.
    }

    [RelayCommand]
    private void InvertSelection(IEnumerable<T>? all)
    {
        if (all is null) return;
        foreach (var item in all)
            item.IsSelected = !item.IsSelected;
    }
}
