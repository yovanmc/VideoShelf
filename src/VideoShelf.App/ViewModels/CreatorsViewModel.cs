using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using VideoShelf.App.Services;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.ViewModels;

public partial class CreatorsViewModel : ObservableObject, IBulkSelectionSource
{
    private readonly LibraryRepository _library;
    private readonly CreatorArtRepository _art;
    private readonly IThumbnailService _thumbnails;

    public CreatorsViewModel(LibraryRepository library, CreatorArtRepository art, IThumbnailService thumbnails)
    {
        _library = library;
        _art = art;
        _thumbnails = thumbnails;
        Selection.SelectedItems.CollectionChanged += (_, _) => SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// B3 — Expands the currently selected creators into their constituent (non-missing) video ids.
    /// Uses <see cref="LibraryRepository.GetEpisodesForSection"/> so no new query method is needed.
    /// Call this each time the selection changes to feed <see cref="BulkActionBarViewModel.SetVideoIds"/>.
    /// </summary>
    public IReadOnlyList<long> GetSelectedVideoIds()
    {
        var ids = new List<long>();
        foreach (var card in Selection.SelectedItems)
        {
            var episodes = _library.GetEpisodesForSection(card.SectionId);
            ids.AddRange(episodes.Select(e => e.VideoId));
        }
        return ids;
    }

    public ObservableCollection<CreatorCardViewModel> Creators { get; } = new();

    /// <summary>Per-page selection state (enter/exit mode, selected set, commands).</summary>
    public SelectionViewModel<CreatorCardViewModel> Selection { get; } = new();

    // ── IBulkSelectionSource ─────────────────────────────────────────────────
    bool IBulkSelectionSource.HasSelection => Selection.HasSelection;
    IReadOnlyList<long> IBulkSelectionSource.GetSelectedVideoIds() => GetSelectedVideoIds();
    public event EventHandler? SelectionChanged;
    void IBulkSelectionSource.ClearSelection() => Selection.ClearSelectionCommand.Execute(null);
    void IBulkSelectionSource.ExitSelectionMode() => Selection.ExitSelectionModeCommand.Execute(null);

    /// <summary>Raised when a creator card is activated (forwarded to the host nav).</summary>
    public event Action<long>? OpenCreatorRequested;

    public async Task LoadAsync(CancellationToken ct)
    {
        // Heavy work off the UI thread; resume on the captured context to mutate the UI-bound collection.
        // NOTE: do NOT use ConfigureAwait(false) on this chain (the Cross-thread ObservableCollection gotcha).
        // GetArtPath is also resolved in the same background Task.Run to avoid per-card SQLite round-trips
        // on the UI thread.
        var cards = await Task.Run(() =>
        {
            var summaries = _library.GetSectionSummaries();
            var result = new System.Collections.Generic.List<(VideoShelf.Core.Models.SectionSummary Summary, string? OverridePath)>(summaries.Count);
            foreach (var s in summaries)
                result.Add((s, _art.GetArtPath(s.SectionId)));
            return result;
        }, ct);

        // Unsubscribe from all existing cards before clearing.
        foreach (var existing in Creators)
            existing.PropertyChanged -= OnCardPropertyChanged;

        Creators.Clear();
        foreach (var (summary, overridePath) in cards)
        {
            var card = new CreatorCardViewModel(summary, overridePath, _thumbnails);
            card.OpenRequested += id => OpenCreatorRequested?.Invoke(id);
            // Subscribe to route IsSelected changes into the Selection VM (no back-ref in card).
            card.PropertyChanged += OnCardPropertyChanged;
            Creators.Add(card);
            await card.LoadImageAsync(ct);
        }
    }

    private void OnCardPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CreatorCardViewModel.IsSelected) &&
            sender is CreatorCardViewModel card)
        {
            Selection.OnItemSelectionChanged(card);
        }
    }
}
