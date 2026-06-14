using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.ViewModels;

/// <summary>Lightweight playlist reference shared across episode rows for "add to playlist" menus.</summary>
public sealed record PlaylistRef(long Id, string Name);

// ── Child view-models ─────────────────────────────────────────────────────────

public sealed partial class PlaylistListItemViewModel(long id, string name, int itemCount)
    : ObservableObject
{
    public long Id { get; } = id;
    public int ItemCount { get; private set; } = itemCount;

    [ObservableProperty]
    private string _name = name;
}

public sealed class PlaylistItemRowViewModel(Core.Models.EpisodeView episode)
{
    public long VideoId => episode.VideoId;
    public string Title => episode.Title;
    public Core.Models.EpisodeView Episode => episode;
}

// ── Main view-model ───────────────────────────────────────────────────────────

public sealed partial class PlaylistsViewModel(
    PlaylistRepository playlists,
    PlayQueueViewModel playQueue) : ObservableObject
{
    public ObservableCollection<PlaylistListItemViewModel> Playlists { get; } = [];
    public ObservableCollection<PlaylistItemRowViewModel> Items { get; } = [];

    /// <summary>True while Load is in progress; used to show the skeleton overlay.
    /// Synchronous loads complete so fast this rarely stays true for a visible frame — that's fine.</summary>
    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelected))]
    private PlaylistListItemViewModel? _selected;

    public bool HasSelected => Selected is not null;

    // ── Load ──────────────────────────────────────────────────────────────────

    public void Load()
    {
        IsLoading = true;
        try
        {
            var all = playlists.GetAll();
            Playlists.Clear();
            foreach (var p in all)
                Playlists.Add(new PlaylistListItemViewModel(p.Id, p.Name, p.ItemCount));
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private void OpenPlaylist(PlaylistListItemViewModel item)
    {
        Selected = item;
        RefreshItems();
    }

    [RelayCommand]
    private void CreatePlaylist()
    {
        var id = playlists.Create("New playlist", DateTimeOffset.UtcNow);
        Load();
        // Auto-select the new playlist
        foreach (var p in Playlists)
        {
            if (p.Id == id) { OpenPlaylist(p); break; }
        }
    }

    [RelayCommand]
    private void RenameSelected(string name)
    {
        if (Selected is null || string.IsNullOrWhiteSpace(name)) return;
        playlists.Rename(Selected.Id, name);
        Selected.Name = name;
    }

    [RelayCommand]
    private void DeletePlaylist(PlaylistListItemViewModel item)
    {
        playlists.Delete(item.Id);
        if (Selected?.Id == item.Id)
        {
            Selected = null;
            Items.Clear();
        }
        Load();
    }

    [RelayCommand]
    private void RemoveItem(PlaylistItemRowViewModel row)
    {
        if (Selected is null) return;
        playlists.RemoveItem(Selected.Id, row.VideoId);
        RefreshItems();
    }

    [RelayCommand]
    private void MoveItemUp(PlaylistItemRowViewModel row)
    {
        if (Selected is null) return;
        var idx = IndexOf(row);
        if (idx <= 0) return;
        playlists.Move(Selected.Id, row.VideoId, idx - 1);
        RefreshItems();
    }

    [RelayCommand]
    private void MoveItemDown(PlaylistItemRowViewModel row)
    {
        if (Selected is null) return;
        var idx = IndexOf(row);
        if (idx < 0 || idx >= Items.Count - 1) return;
        playlists.Move(Selected.Id, row.VideoId, idx + 1);
        RefreshItems();
    }

    [RelayCommand]
    private void PlayAll()
    {
        if (Selected is null) return;
        var items = playlists.GetItems(Selected.Id);
        if (items.Count > 0)
            playQueue.PlayAll(items);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void RefreshItems()
    {
        Items.Clear();
        if (Selected is null) return;
        foreach (var ep in playlists.GetItems(Selected.Id))
            Items.Add(new PlaylistItemRowViewModel(ep));
    }

    private int IndexOf(PlaylistItemRowViewModel row)
    {
        for (int i = 0; i < Items.Count; i++)
            if (Items[i].VideoId == row.VideoId) return i;
        return -1;
    }
}
