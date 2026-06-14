using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.ViewModels;

/// <summary>A single row in the watch history list.</summary>
public sealed partial class HistoryRowViewModel(HistoryEntry entry) : ObservableObject
{
    public long VideoId => entry.VideoId;

    /// <summary>Standalone → just the series title; episode → "Series · Episode N".</summary>
    public string Title => entry.IsStandalone
        ? entry.SeriesTitle
        : $"{entry.SeriesTitle} · Episode {entry.EpisodeNo}";

    /// <summary>ISO 8601 timestamp parsed and formatted as a local short date-time string.</summary>
    public string WatchedAt
    {
        get
        {
            if (DateTimeOffset.TryParse(entry.WatchedAt, out var dto))
                return dto.LocalDateTime.ToString("g"); // e.g. "6/13/2026 3:42 PM"
            return entry.WatchedAt;
        }
    }

    public event EventHandler<long>? PlayInvoked;

    [RelayCommand]
    private void Play() => PlayInvoked?.Invoke(this, VideoId);
}

/// <summary>ViewModel for the watch-history page.</summary>
public sealed partial class HistoryViewModel(HistoryRepository history, LibraryRepository library) : ObservableObject
{
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
            var rows = await Task.Run(() => history.GetHistory(100));

            Entries.Clear();
            foreach (var row in rows)
            {
                var rowVm = new HistoryRowViewModel(row);
                var capturedId = row.VideoId;
                rowVm.PlayInvoked += (_, _) =>
                {
                    var ep = library.GetEpisode(capturedId);
                    if (ep is not null) PlayRequested?.Invoke(this, ep);
                };
                Entries.Add(rowVm);
            }
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
