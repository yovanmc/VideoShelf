using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoShelf.App.Services;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.ViewModels;

/// <summary>A single row in the watch history list, including cover art, progress, and date-group key.</summary>
public sealed partial class HistoryRowViewModel : ObservableObject
{
    private readonly HistoryEntry _entry;
    private readonly IThumbnailService? _thumbnails;
    private readonly IImageLoader? _imageLoader;

    public HistoryRowViewModel(HistoryEntry entry,
        double? duration = null, double resumePosition = 0,
        IThumbnailService? thumbnails = null, IImageLoader? imageLoader = null)
    {
        _entry = entry;
        Duration = duration;
        ResumePosition = resumePosition;
        _thumbnails = thumbnails;
        _imageLoader = imageLoader;
    }

    public long VideoId => _entry.VideoId;

    /// <summary>Standalone → just the series title; episode → "Series · Episode N".</summary>
    public string Title => _entry.IsStandalone
        ? _entry.SeriesTitle
        : $"{_entry.SeriesTitle} · Episode {_entry.EpisodeNo}";

    /// <summary>ISO 8601 timestamp parsed and formatted as a local short date-time string.</summary>
    public string WatchedAt
    {
        get
        {
            if (DateTimeOffset.TryParse(_entry.WatchedAt, out var dto))
                return dto.LocalDateTime.ToString("g"); // e.g. "6/13/2026 3:42 PM"
            return _entry.WatchedAt;
        }
    }

    /// <summary>Date-group key: "Today", "This week", or "Older".</summary>
    public string DateGroup
    {
        get
        {
            if (!DateTimeOffset.TryParse(_entry.WatchedAt, out var dto)) return "Older";
            var now = DateTimeOffset.Now;
            var local = dto.LocalDateTime;
            if (local.Date == now.Date) return "Today";
            if ((now.Date - local.Date).TotalDays < 7) return "This week";
            return "Older";
        }
    }

    // ── Progress (from EpisodeView at row-construction time) ─────────────────

    private double? Duration { get; }
    private double ResumePosition { get; }

    public double ProgressFraction =>
        Duration is > 0 ? Math.Clamp(ResumePosition / Duration.Value, 0, 1) : 0;

    public bool HasProgress => ProgressFraction > 0 && ProgressFraction < 1.0;

    // ── Cover image ──────────────────────────────────────────────────────────

    [ObservableProperty] private ImageSource? _cover;

    /// <summary>Loads the cover image asynchronously from the thumbnail service (fail-safe).</summary>
    public async Task LoadImageAsync(CancellationToken ct)
    {
        if (_thumbnails is null || _imageLoader is null) return;
        if (string.IsNullOrWhiteSpace(_entry.ThumbnailSeedPath)) return;

        var path = await _thumbnails.GetThumbnailPathAsync(_entry.ThumbnailSeedPath!, ct);
        Cover = _imageLoader.Load(path, decodePixelWidth: 200);
    }

    public event EventHandler<long>? PlayInvoked;

    [RelayCommand]
    private void Play() => PlayInvoked?.Invoke(this, VideoId);
}

/// <summary>A date-grouped section header shown above history rows.</summary>
public sealed record HistoryGroupViewModel(string Label, ObservableCollection<HistoryRowViewModel> Rows);

/// <summary>ViewModel for the watch-history page.</summary>
public sealed partial class HistoryViewModel : ObservableObject
{
    private readonly HistoryRepository _history;
    private readonly LibraryRepository _library;
    private readonly IThumbnailService? _thumbnails;
    private readonly IImageLoader? _imageLoader;

    public HistoryViewModel(HistoryRepository history, LibraryRepository library,
        IThumbnailService? thumbnails = null, IImageLoader? imageLoader = null)
    {
        _history = history;
        _library = library;
        _thumbnails = thumbnails;
        _imageLoader = imageLoader;
    }

    /// <summary>Pre-grouped rows: Today / This week / Older. Bound by the view's ItemsControl.</summary>
    public ObservableCollection<HistoryGroupViewModel> Groups { get; } = [];

    // Keep Entries for backward-compat (tests) — derived flat list.
    public ObservableCollection<HistoryRowViewModel> Entries { get; } = [];

    public bool HasHistory => Entries.Count > 0;

    /// <summary>True while LoadAsync is in progress; used to show the skeleton overlay.</summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>Raised when a row's Play is invoked; carries the resolved EpisodeView.</summary>
    public event EventHandler<EpisodeView>? PlayRequested;

    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var rows = await Task.Run(() => _history.GetHistory(100));

            Entries.Clear();
            Groups.Clear();

            // Temporary grouping buckets
            var today = new ObservableCollection<HistoryRowViewModel>();
            var thisWeek = new ObservableCollection<HistoryRowViewModel>();
            var older = new ObservableCollection<HistoryRowViewModel>();

            using var cts = new CancellationTokenSource();

            foreach (var row in rows)
            {
                // Get episode for play + progress metadata
                var ep = _library.GetEpisode(row.VideoId);

                var rowVm = new HistoryRowViewModel(
                    row,
                    duration: ep?.Duration,
                    resumePosition: ep?.ResumePosition ?? 0,
                    thumbnails: _thumbnails,
                    imageLoader: _imageLoader);

                var capturedEp = ep;
                rowVm.PlayInvoked += (_, _) =>
                {
                    if (capturedEp is not null) PlayRequested?.Invoke(this, capturedEp);
                };

                Entries.Add(rowVm);

                switch (rowVm.DateGroup)
                {
                    case "Today":    today.Add(rowVm);   break;
                    case "This week": thisWeek.Add(rowVm); break;
                    default:         older.Add(rowVm);   break;
                }

                // Load cover in background (fire-and-forget per row, fail-safe)
                _ = rowVm.LoadImageAsync(cts.Token);
            }

            if (today.Count > 0)    Groups.Add(new HistoryGroupViewModel("Today",     today));
            if (thisWeek.Count > 0) Groups.Add(new HistoryGroupViewModel("This week", thisWeek));
            if (older.Count > 0)    Groups.Add(new HistoryGroupViewModel("Older",     older));

            OnPropertyChanged(nameof(HasHistory));
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Synchronous wrapper for use from MainViewModel RelayCommand.</summary>
    public void Load() => _ = LoadAsync();
}
