using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoShelf.App.Motion;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.ViewModels;

/// <summary>
/// Selection-source-agnostic bulk-action bar. The hosting page resolves
/// creator/card selections into video ids and sets <see cref="VideoIds"/>
/// before invoking any command. After each action a <see cref="Completed"/>
/// event is raised so the page can refresh affected rows and/or exit
/// selection mode.
/// </summary>
public sealed partial class BulkActionBarViewModel : ObservableObject
{
    private readonly WatchRepository _watch;
    private readonly TagRepository _tags;
    private readonly CurationRepository _curation;
    private readonly PlaylistRepository _playlists;
    private readonly PlayQueueViewModel _queue;
    private readonly LibraryRepository _library;
    private readonly IToastService? _toasts;

    public BulkActionBarViewModel(
        WatchRepository watch,
        TagRepository tags,
        CurationRepository curation,
        PlaylistRepository playlists,
        PlayQueueViewModel queue,
        LibraryRepository library,
        IToastService? toasts = null)
    {
        _watch = watch;
        _tags = tags;
        _curation = curation;
        _playlists = playlists;
        _queue = queue;
        _library = library;
        _toasts = toasts;
    }

    // ── Selection input ──────────────────────────────────────────────────────

    /// <summary>
    /// The video ids currently selected. Set by the hosting page each time
    /// the selection changes (e.g. in <see cref="CreatorsViewModel"/> via
    /// GetEpisodesForSection fan-out). Commands operate over these ids.
    /// </summary>
    public IReadOnlyList<long> VideoIds { get; private set; } = Array.Empty<long>();

    public void SetVideoIds(IReadOnlyList<long> ids)
    {
        VideoIds = ids ?? Array.Empty<long>();
        OnPropertyChanged(nameof(VideoIds));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedCountLabel));
    }

    public int SelectedCount => VideoIds.Count;
    public string SelectedCountLabel => SelectedCount == 1 ? "1 selected" : $"{SelectedCount} selected";

    // ── Completed event ──────────────────────────────────────────────────────

    /// <summary>
    /// Raised after any bulk action completes. The page should refresh
    /// affected rows and may exit selection mode.
    /// </summary>
    public event EventHandler? Completed;

    private void RaiseCompleted() => Completed?.Invoke(this, EventArgs.Empty);

    // ── Inline tag entry ─────────────────────────────────────────────────────

    [ObservableProperty]
    private string _pendingTag = string.Empty;

    // ── Available playlists ──────────────────────────────────────────────────

    /// <summary>Populated on demand; drives the "Add to playlist" picker.</summary>
    public ObservableCollection<Playlist> AvailablePlaylists { get; } = new();

    /// <summary>Refreshes <see cref="AvailablePlaylists"/> from the repository.</summary>
    public void RefreshPlaylists()
    {
        AvailablePlaylists.Clear();
        foreach (var p in _playlists.GetAll())
            AvailablePlaylists.Add(p);
    }

    // ── Commands ─────────────────────────────────────────────────────────────

    [RelayCommand]
    private void MarkWatched()
    {
        var ids = VideoIds.ToList();
        foreach (var id in ids)
            _watch.SetWatched(id, true);
        RaiseCompleted();
        _toasts?.Show($"Marked {ids.Count} watched",
                      undo: () => MarkUnwatchedIds(ids), ToastKind.Success);
    }

    [RelayCommand]
    private void MarkUnwatched()
    {
        var ids = VideoIds.ToList();
        foreach (var id in ids)
            _watch.SetWatched(id, false);
        RaiseCompleted();
        _toasts?.Show($"Marked {ids.Count} unwatched",
                      undo: () => MarkWatchedIds(ids), ToastKind.Success);
    }

    /// <summary>Inverse of MarkWatched: marks a snapshot of ids unwatched. Used as undo callback.</summary>
    internal void MarkUnwatchedIds(IReadOnlyList<long> ids)
    {
        foreach (var id in ids)
            _watch.SetWatched(id, false);
        RaiseCompleted();
    }

    /// <summary>Inverse of MarkUnwatched: marks a snapshot of ids watched. Used as undo callback.</summary>
    internal void MarkWatchedIds(IReadOnlyList<long> ids)
    {
        foreach (var id in ids)
            _watch.SetWatched(id, true);
        RaiseCompleted();
    }

    /// <summary>
    /// Adds <see cref="PendingTag"/> to every selected video.
    /// <see cref="TagRepository.AddVideoTag"/> handles normalization internally.
    /// No undo (no bulk remove-tag inverse today) — informational toast only.
    /// </summary>
    [RelayCommand]
    private void ApplyTag()
    {
        var tag = PendingTag.Trim();
        if (string.IsNullOrEmpty(tag)) return;
        var ids = VideoIds.ToList();
        foreach (var id in ids)
            _tags.AddVideoTag(id, tag);
        PendingTag = string.Empty;
        RaiseCompleted();
        _toasts?.Show($"Tag \"{tag}\" applied to {ids.Count} video(s)");
    }

    [RelayCommand]
    private void AddToPlaylist(Playlist? playlist)
    {
        if (playlist is null) return;
        var ids = VideoIds.ToList();
        foreach (var id in ids)
            _playlists.AddItem(playlist.Id, id);
        RaiseCompleted();
        // No simple inverse (items can't be un-added without knowing their row id) — informational only.
        _toasts?.Show($"Added {ids.Count} to playlist");
    }

    /// <summary>
    /// Resolves each video id to an <see cref="EpisodeView"/> and enqueues it.
    /// Missing videos (GetEpisode returns null) are silently skipped.
    /// No undo (queue position is ephemeral) — informational toast only.
    /// </summary>
    [RelayCommand]
    private void AddToQueue()
    {
        var ids = VideoIds.ToList();
        foreach (var id in ids)
        {
            var ep = _library.GetEpisode(id);
            if (ep is not null)
                _queue.Enqueue(ep);
        }
        RaiseCompleted();
        _toasts?.Show($"Added {ids.Count} to queue");
    }

    [RelayCommand]
    private void AddFavorite()
    {
        var ids = VideoIds.ToList();
        foreach (var id in ids)
            _curation.SetFavorite(id, true);
        RaiseCompleted();
        _toasts?.Show($"Favorited {ids.Count} video(s)",
                      undo: () => RemoveFavoriteIds(ids), ToastKind.Success);
    }

    [RelayCommand]
    private void RemoveFavorite()
    {
        var ids = VideoIds.ToList();
        foreach (var id in ids)
            _curation.SetFavorite(id, false);
        RaiseCompleted();
        _toasts?.Show($"Unfavorited {ids.Count} video(s)",
                      undo: () => AddFavoriteIds(ids), ToastKind.Success);
    }

    /// <summary>Inverse of RemoveFavorite: adds favorite back for a snapshot of ids.</summary>
    internal void AddFavoriteIds(IReadOnlyList<long> ids)
    {
        foreach (var id in ids) _curation.SetFavorite(id, true);
        RaiseCompleted();
    }

    /// <summary>Inverse of AddFavorite: removes favorite for a snapshot of ids.</summary>
    internal void RemoveFavoriteIds(IReadOnlyList<long> ids)
    {
        foreach (var id in ids) _curation.SetFavorite(id, false);
        RaiseCompleted();
    }

    [RelayCommand]
    private void AddToWatchlist()
    {
        var ids = VideoIds.ToList();
        var now = DateTimeOffset.UtcNow;
        foreach (var id in ids)
            _curation.SetWatchlist(id, true, now);
        RaiseCompleted();
        _toasts?.Show($"Added {ids.Count} to watchlist",
                      undo: () => RemoveFromWatchlistIds(ids), ToastKind.Success);
    }

    [RelayCommand]
    private void RemoveFromWatchlist()
    {
        var ids = VideoIds.ToList();
        var now = DateTimeOffset.UtcNow;
        foreach (var id in ids)
            _curation.SetWatchlist(id, false, now);
        RaiseCompleted();
        _toasts?.Show($"Removed {ids.Count} from watchlist",
                      undo: () => AddToWatchlistIds(ids), ToastKind.Success);
    }

    /// <summary>Inverse of AddToWatchlist: removes from watchlist for a snapshot of ids.</summary>
    internal void RemoveFromWatchlistIds(IReadOnlyList<long> ids)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var id in ids) _curation.SetWatchlist(id, false, now);
        RaiseCompleted();
    }

    /// <summary>Inverse of RemoveFromWatchlist: adds to watchlist for a snapshot of ids.</summary>
    internal void AddToWatchlistIds(IReadOnlyList<long> ids)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var id in ids) _curation.SetWatchlist(id, true, now);
        RaiseCompleted();
    }
}
