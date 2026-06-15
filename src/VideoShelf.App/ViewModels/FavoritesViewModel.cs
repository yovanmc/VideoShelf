using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using VideoShelf.App.Services;
using VideoShelf.App.ViewModels.Discovery;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.ViewModels;

public sealed partial class FavoritesViewModel(CurationRepository curation, LibraryRepository library,
    IThumbnailService? thumbnails = null, IImageLoader? imageLoader = null) : ObservableObject, IBulkSelectionSource
{
    public ObservableCollection<RecencyCardViewModel> Favorites { get; } = [];

    public bool HasFavorites => Favorites.Count > 0;

    /// <summary>True while LoadAsync is in progress; used to show the skeleton overlay.</summary>
    [ObservableProperty]
    private bool _isLoading;

    private readonly SelectionViewModel<RecencyCardViewModel> _selection = new();

    /// <summary>Per-page selection state for multi-select over the favorites grid.</summary>
    public SelectionViewModel<RecencyCardViewModel> Selection => _selection;

    // ── IBulkSelectionSource ─────────────────────────────────────────────────
    bool IBulkSelectionSource.HasSelection => Selection.HasSelection;
    IReadOnlyList<long> IBulkSelectionSource.GetSelectedVideoIds() => GetSelectedVideoIds();
    public event EventHandler? SelectionChanged;
    void IBulkSelectionSource.ClearSelection() => Selection.ClearSelectionCommand.Execute(null);
    void IBulkSelectionSource.ExitSelectionMode() => Selection.ExitSelectionModeCommand.Execute(null);

    /// <summary>Returns video ids for all currently selected cards.</summary>
    public IReadOnlyList<long> GetSelectedVideoIds()
        => Selection.SelectedItems.Select(c => c.VideoId).ToList();

    /// <summary>Raised when a card's Play is invoked; carries the resolved EpisodeView.</summary>
    public event EventHandler<EpisodeView>? PlayRequested;

    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
        // Unsubscribe from existing cards before clearing.
        foreach (var existing in Favorites)
            existing.PropertyChanged -= OnCardPropertyChanged;

        Selection.ExitSelectionModeCommand.Execute(null);

        var items = await Task.Run(() => curation.GetFavorites(48));

        Favorites.Clear();
        foreach (var item in items)
        {
            var card = new RecencyCardViewModel(item, thumbnails, imageLoader);
            var capturedId = item.VideoId;
            card.PlayInvoked += (_, _) =>
            {
                var ep = library.GetEpisode(capturedId);
                if (ep is not null) PlayRequested?.Invoke(this, ep);
            };
            card.PropertyChanged += OnCardPropertyChanged;
            Favorites.Add(card);
            _ = card.LoadImageAsync(CancellationToken.None);
        }
        OnPropertyChanged(nameof(HasFavorites));
        } // end try
        finally
        {
            IsLoading = false;
        }
    }

    private void OnCardPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RecencyCardViewModel.IsSelected) &&
            sender is RecencyCardViewModel card)
        {
            Selection.OnItemSelectionChanged(card);
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Synchronous wrapper for use from MainViewModel RelayCommand.</summary>
    public void Load() => _ = LoadAsync();
}
