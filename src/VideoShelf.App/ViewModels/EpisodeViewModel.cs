using System.Collections.Generic;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoShelf.App.Motion;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.ViewModels;

public sealed partial class EpisodeViewModel(
    EpisodeView model,
    WatchRepository watch,
    TagRepository? tags = null,
    CurationRepository? curation = null,
    PlaylistRepository? playlists = null,
    IReadOnlyList<PlaylistRef>? availablePlaylists = null,
    IToastService? toasts = null) : ObservableObject, ISelectableCard
{
    public TagEditorViewModel? VideoTagEditor { get; } = tags != null ? new TagEditorViewModel(tags) : null;

    public long VideoId => model.VideoId;
    public string Title => model.Title;
    public int EpisodeNo => model.EpisodeNo;
    public string FilePath => model.FilePath;
    public bool IsMissing => model.Missing;

    // --- playback progress (C2) ---
    public double? Duration => model.Duration;
    public double ResumePosition => model.ResumePosition;

    /// <summary>0..1 fraction of the episode watched; 0 when duration is unknown.</summary>
    public double ProgressFraction =>
        model.Duration is > 0 ? System.Math.Clamp(model.ResumePosition / model.Duration.Value, 0, 1) : 0;

    /// <summary>True when there is non-trivial in-progress resume position (not started or fully watched).</summary>
    public bool HasProgress => ProgressFraction is > 0 and < 1;

    /// <summary>Human-readable runtime: "h:mm" when ≥1 hour, "m:ss" otherwise. Null when duration unknown.</summary>
    public string? RuntimeLabel => model.Duration is double d ? FormatRuntime(d) : null;

    private static string FormatRuntime(double seconds)
    {
        var totalSeconds = (int)System.Math.Round(seconds);
        var h = totalSeconds / 3600;
        var m = (totalSeconds % 3600) / 60;
        var s = totalSeconds % 60;
        return h >= 1 ? $"{h}:{m:D2}" : $"{m}:{s:D2}";
    }

    public bool HasCuration => curation is not null;

    /// <summary>True when this episode is selected in the multi-select list.
    /// The hosting VM subscribes to PropertyChanged and routes changes to
    /// <see cref="SelectionViewModel{T}.OnItemSelectionChanged"/> — no back-ref is stored here.</summary>
    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _watched = model.Watched;

    [ObservableProperty]
    private bool _isFavorite = curation?.IsFavorite(model.VideoId) ?? false;

    [ObservableProperty]
    private bool _inWatchlist = curation?.InWatchlist(model.VideoId) ?? false;

    [ObservableProperty]
    private double _rating = curation?.GetRating(model.VideoId) ?? 0.0;

    public event System.EventHandler? WatchedChanged;

    [RelayCommand]
    private void ToggleWatched()
    {
        Watched = !Watched;
        watch.SetWatched(model.VideoId, Watched);
        WatchedChanged?.Invoke(this, System.EventArgs.Empty);
    }

    [RelayCommand]
    private void ToggleFavorite()
    {
        if (curation is null) return;
        IsFavorite = !IsFavorite;
        curation.SetFavorite(model.VideoId, IsFavorite);
        var wasAdded = IsFavorite; // capture resulting state for undo closure
        toasts?.Show(wasAdded ? "Added to favorites" : "Removed from favorites",
                     undo: () => SetFavoriteDirectly(!wasAdded), ToastKind.Success);
    }

    /// <summary>Direct state+DB inverse used by the undo callback — does NOT show another toast.</summary>
    private void SetFavoriteDirectly(bool value)
    {
        IsFavorite = value;
        curation?.SetFavorite(model.VideoId, value);
    }

    [RelayCommand]
    private void ToggleWatchlist()
    {
        if (curation is null) return;
        InWatchlist = !InWatchlist;
        curation.SetWatchlist(model.VideoId, InWatchlist, System.DateTimeOffset.UtcNow);
        var wasAdded = InWatchlist; // capture resulting state for undo closure
        toasts?.Show(wasAdded ? "Added to watchlist" : "Removed from watchlist",
                     undo: () => SetWatchlistDirectly(!wasAdded), ToastKind.Success);
    }

    /// <summary>Direct state+DB inverse used by the undo callback — does NOT show another toast.</summary>
    private void SetWatchlistDirectly(bool value)
    {
        InWatchlist = value;
        curation?.SetWatchlist(model.VideoId, value, System.DateTimeOffset.UtcNow);
    }

    [RelayCommand]
    private void SetRating(object? param)
    {
        if (curation is null) return;
        double r = param switch
        {
            double d => d,
            int i => (double)i,
            string s when double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => 0.0,
        };
        var clamped = System.Math.Clamp(r, 0.0, 5.0);
        Rating = clamped;
        curation.SetRating(model.VideoId, clamped);
    }

    /// <summary>The shared list of available playlists to add this episode to (empty if no PlaylistRepository).</summary>
    public IReadOnlyList<PlaylistRef> AvailablePlaylists => availablePlaylists ?? [];

    [RelayCommand]
    private void AddToPlaylist(object? param)
    {
        if (playlists is null) return;
        var playlistId = param switch
        {
            long l => l,
            int i => (long)i,
            string s when long.TryParse(s, out var parsed) => parsed,
            _ => -1L,
        };
        if (playlistId < 0) return;
        playlists.AddItem(playlistId, model.VideoId);
    }

    /// <summary>Raised when the user asks to play this episode; the shell routes it to the player.</summary>
    public event System.EventHandler<EpisodeView>? PlayRequested;

    [RelayCommand]
    private void Play() => PlayRequested?.Invoke(this, model);
}
