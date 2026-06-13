using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoShelf.App.Services;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.ViewModels;

public sealed partial class SeriesViewModel(
    SeriesSummary summary,
    LibraryRepository library,
    WatchRepository watch,
    IThumbnailService thumbnails,
    TagRepository? tags = null,
    CurationRepository? curation = null,
    PlaylistRepository? playlists = null,
    IReadOnlyList<PlaylistRef>? availablePlaylists = null) : ObservableObject
{
    public TagEditorViewModel? SeriesTagEditor { get; } = tags != null ? new TagEditorViewModel(tags) : null;

    public long SeriesId => summary.SeriesId;
    public string BaseTitle => summary.BaseTitle;
    public bool IsStandalone => summary.IsStandalone;
    public int EpisodeCount => summary.EpisodeCount;

    public ObservableCollection<EpisodeViewModel> Episodes { get; } = [];

    [ObservableProperty]
    private int _unwatchedCount = summary.UnwatchedCount;

    [ObservableProperty]
    private string? _thumbnailPath;

    [ObservableProperty]
    private bool _isExpanded;

    private bool _episodesLoaded;

    public string EpisodeCountLabel => IsStandalone ? "Standalone" : $"{EpisodeCount} episodes";

    public bool HasUnwatched => UnwatchedCount > 0;

    public event System.EventHandler? UnwatchedChanged;
    public event System.EventHandler<EpisodeView>? PlayRequested;
    public event System.EventHandler<SeriesViewModel>? RenameRequested;
    public event System.EventHandler? PlayAllRequested;
    public event System.EventHandler? EnqueueRequested;
    public event System.EventHandler? PlayNextRequested;
    public event System.EventHandler? MarkWatchedRequested;
    public event System.EventHandler? MarkUnwatchedRequested;

    [RelayCommand]
    private void RequestRename() => RenameRequested?.Invoke(this, this);

    [RelayCommand]
    private void PlayAllSeries() => PlayAllRequested?.Invoke(this, System.EventArgs.Empty);

    [RelayCommand]
    private void AddSeriesToQueue() => EnqueueRequested?.Invoke(this, System.EventArgs.Empty);

    [RelayCommand]
    private void PlaySeriesNext() => PlayNextRequested?.Invoke(this, System.EventArgs.Empty);

    [RelayCommand]
    private void MarkSeriesWatched() => MarkWatchedRequested?.Invoke(this, System.EventArgs.Empty);

    [RelayCommand]
    private void MarkSeriesUnwatched() => MarkUnwatchedRequested?.Invoke(this, System.EventArgs.Empty);

    [RelayCommand]
    private async Task Activate()
    {
        if (IsStandalone)
        {
            await EnsureEpisodesLoadedAsync();
            Episodes.FirstOrDefault()?.PlayCommand.Execute(null);   // raises PlayRequested via the episode
            return;
        }
        IsExpanded = !IsExpanded;
        if (IsExpanded) await EnsureEpisodesLoadedAsync();
    }

    private async Task EnsureEpisodesLoadedAsync()
    {
        if (_episodesLoaded) return;
        await LoadEpisodesAsync(CancellationToken.None);
        _episodesLoaded = true;
        // Load tag editors on the UI thread (post-await, NOT inside Task.Run).
        if (SeriesTagEditor != null)
            SeriesTagEditor.Load(TagLevel.Series, SeriesId);
        foreach (var ep in Episodes)
            ep.VideoTagEditor?.Load(TagLevel.Video, ep.VideoId);
    }

    partial void OnUnwatchedCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasUnwatched));
        UnwatchedChanged?.Invoke(this, System.EventArgs.Empty);
    }

    /// <summary>Recomputes the unwatched badge from the DB (after a watched toggle).</summary>
    public void Refresh()
    {
        var fresh = 0;
        foreach (var e in library.GetEpisodes(summary.SeriesId))
            if (!e.Watched) fresh++;
        UnwatchedCount = fresh;
    }

    public async Task LoadEpisodesAsync(CancellationToken cancellationToken)
    {
        var rows = await Task.Run(() => library.GetEpisodes(summary.SeriesId), cancellationToken);
        Episodes.Clear();
        foreach (var row in rows)
        {
            var ep = new EpisodeViewModel(row, watch, tags, curation, playlists, availablePlaylists);
            ep.WatchedChanged += (_, _) => Refresh();
            ep.PlayRequested += (_, e) => PlayRequested?.Invoke(this, e);
            Episodes.Add(ep);
        }
    }

    public async Task LoadThumbnailAsync(CancellationToken cancellationToken)
    {
        if (summary.ThumbnailSeedPath is null)
            return;
        ThumbnailPath = await thumbnails.GetThumbnailPathAsync(summary.ThumbnailSeedPath, cancellationToken);
    }
}
