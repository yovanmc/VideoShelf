using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    public BulkActionBarViewModel(
        WatchRepository watch,
        TagRepository tags,
        CurationRepository curation,
        PlaylistRepository playlists,
        PlayQueueViewModel queue,
        LibraryRepository library)
    {
        _watch = watch;
        _tags = tags;
        _curation = curation;
        _playlists = playlists;
        _queue = queue;
        _library = library;
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
        foreach (var id in VideoIds)
            _watch.SetWatched(id, true);
        RaiseCompleted();
    }

    [RelayCommand]
    private void MarkUnwatched()
    {
        foreach (var id in VideoIds)
            _watch.SetWatched(id, false);
        RaiseCompleted();
    }

    /// <summary>
    /// Adds <see cref="PendingTag"/> to every selected video.
    /// <see cref="TagRepository.AddVideoTag"/> handles normalization internally.
    /// </summary>
    [RelayCommand]
    private void ApplyTag()
    {
        var tag = PendingTag.Trim();
        if (string.IsNullOrEmpty(tag)) return;
        foreach (var id in VideoIds)
            _tags.AddVideoTag(id, tag);
        PendingTag = string.Empty;
        RaiseCompleted();
    }

    [RelayCommand]
    private void AddToPlaylist(Playlist? playlist)
    {
        if (playlist is null) return;
        foreach (var id in VideoIds)
            _playlists.AddItem(playlist.Id, id);
        RaiseCompleted();
    }

    /// <summary>
    /// Resolves each video id to an <see cref="EpisodeView"/> and enqueues it.
    /// Missing videos (GetEpisode returns null) are silently skipped.
    /// </summary>
    [RelayCommand]
    private void AddToQueue()
    {
        foreach (var id in VideoIds)
        {
            var ep = _library.GetEpisode(id);
            if (ep is not null)
                _queue.Enqueue(ep);
        }
        RaiseCompleted();
    }

    [RelayCommand]
    private void AddFavorite()
    {
        foreach (var id in VideoIds)
            _curation.SetFavorite(id, true);
        RaiseCompleted();
    }

    [RelayCommand]
    private void RemoveFavorite()
    {
        foreach (var id in VideoIds)
            _curation.SetFavorite(id, false);
        RaiseCompleted();
    }

    [RelayCommand]
    private void AddToWatchlist()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var id in VideoIds)
            _curation.SetWatchlist(id, true, now);
        RaiseCompleted();
    }

    [RelayCommand]
    private void RemoveFromWatchlist()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var id in VideoIds)
            _curation.SetWatchlist(id, false, now);
        RaiseCompleted();
    }
}
