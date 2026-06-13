using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.ViewModels;

/// <summary>
/// Ephemeral, in-memory up-next queue. The currently-playing item, when it
/// originates from the queue, is Items[CurrentIndex]. IsExplicitQueue is true
/// when the user built a real queue (Play all / Add to queue / Play next) and
/// gates the queue UI + the queue-first end-of-media behaviour. A plain single
/// play (StartSingle) is a non-explicit queue-of-one that preserves legacy
/// single-series auto-advance. The queue is never persisted.
/// </summary>
public sealed partial class PlayQueueViewModel : ObservableObject
{
    private readonly LibraryRepository _library;
    private readonly SettingsRepository _settings;

    public PlayQueueViewModel(LibraryRepository library, SettingsRepository settings)
    {
        _library = library;
        _settings = settings;
    }

    public ObservableCollection<QueueItemViewModel> Items { get; } = new();

    [ObservableProperty] private int _currentIndex = -1;
    [ObservableProperty] private bool _isExplicitQueue;
    [ObservableProperty] private bool _isQueueOpen; // in-player drawer open state

    /// <summary>Host plays this episode without the queue re-touching its own cursor.</summary>
    public event EventHandler<EpisodeView>? PlayRequested;

    /// <summary>True when there is a real, user-built queue to display.</summary>
    public bool HasQueue => IsExplicitQueue && Items.Count > 0;

    /// <summary>"N in queue" label for nav/page headers.</summary>
    public string CountLabel => Items.Count == 1 ? "1 in queue" : $"{Items.Count} in queue";

    partial void OnIsExplicitQueueChanged(bool value) => OnPropertyChanged(nameof(HasQueue));
    partial void OnCurrentIndexChanged(int value) => UpdateNowPlayingFlags();

    private void NotifyCollectionDerived()
    {
        OnPropertyChanged(nameof(HasQueue));
        OnPropertyChanged(nameof(CountLabel));
        UpdateNowPlayingFlags();
    }

    private void UpdateNowPlayingFlags()
    {
        for (int i = 0; i < Items.Count; i++)
            Items[i].IsNowPlaying = i == CurrentIndex;
    }

    // ---- entry: build + start a real queue (Play all / per-series play all) ----
    public void PlayAll(IReadOnlyList<EpisodeView> episodes)
    {
        if (episodes is null || episodes.Count == 0) return;
        Items.Clear();
        foreach (var e in episodes) Items.Add(new QueueItemViewModel(e));
        IsExplicitQueue = true;
        CurrentIndex = 0;
        NotifyCollectionDerived();
        PlayRequested?.Invoke(this, Items[0].Episode);
    }

    // ---- entry: direct single play (a card/episode click, non-queue) ----
    public void StartSingle(EpisodeView episode)
    {
        Items.Clear();
        Items.Add(new QueueItemViewModel(episode));
        IsExplicitQueue = false;
        CurrentIndex = 0;
        NotifyCollectionDerived();
    }

    // ---- enqueue (no immediate playback change) ----
    public void Enqueue(EpisodeView episode)
    {
        Items.Add(new QueueItemViewModel(episode));
        IsExplicitQueue = true;
        NotifyCollectionDerived();
    }

    public void EnqueueRange(IReadOnlyList<EpisodeView> episodes)
    {
        if (episodes is null || episodes.Count == 0) return;
        foreach (var e in episodes) Items.Add(new QueueItemViewModel(e));
        IsExplicitQueue = true;
        NotifyCollectionDerived();
    }

    public void PlayNext(EpisodeView episode)
    {
        var at = CurrentIndex >= 0 ? CurrentIndex + 1 : Items.Count;
        Items.Insert(at, new QueueItemViewModel(episode));
        IsExplicitQueue = true;
        NotifyCollectionDerived();
    }

    public void PlayNextRange(IReadOnlyList<EpisodeView> episodes)
    {
        if (episodes is null || episodes.Count == 0) return;
        var at = CurrentIndex >= 0 ? CurrentIndex + 1 : Items.Count;
        for (int i = 0; i < episodes.Count; i++)
            Items.Insert(at + i, new QueueItemViewModel(episodes[i]));
        IsExplicitQueue = true;
        NotifyCollectionDerived();
    }

    // ---- end-of-media: queue-first, falling back to legacy single-series auto-advance ----
    public EpisodeView? GetNextAfterEnd(EpisodeView finished)
    {
        if (IsExplicitQueue)
        {
            if (CurrentIndex >= 0 && CurrentIndex + 1 < Items.Count)
            {
                CurrentIndex++; // raises UpdateNowPlayingFlags
                return Items[CurrentIndex].Episode;
            }
            // explicit queue exhausted → clear and stop
            Clear();
            return null;
        }
        // non-explicit single play → legacy auto-advance
        if (_settings.GetAutoAdvanceEpisodes())
        {
            var next = _library.GetNextEpisode(finished.SeriesId, finished.EpisodeNo);
            if (next is not null)
            {
                StartSingle(next);
                return next;
            }
        }
        return null;
    }

    // ---- manual controls (bound from drawer + page) ----
    [RelayCommand]
    private void JumpTo(QueueItemViewModel? item)
    {
        if (item is null) return;
        var idx = Items.IndexOf(item);
        if (idx < 0) return;
        IsExplicitQueue = true;
        CurrentIndex = idx;
        PlayRequested?.Invoke(this, item.Episode);
    }

    [RelayCommand]
    private void SkipNext()
    {
        if (CurrentIndex >= 0 && CurrentIndex + 1 < Items.Count)
        {
            CurrentIndex++;
            PlayRequested?.Invoke(this, Items[CurrentIndex].Episode);
        }
    }

    [RelayCommand]
    private void SkipPrevious()
    {
        if (CurrentIndex > 0)
        {
            CurrentIndex--;
            PlayRequested?.Invoke(this, Items[CurrentIndex].Episode);
        }
    }

    [RelayCommand]
    private void RemoveItem(QueueItemViewModel? item)
    {
        if (item is null) return;
        var idx = Items.IndexOf(item);
        if (idx < 0) return;
        Items.RemoveAt(idx);
        if (idx < CurrentIndex) CurrentIndex--;
        else if (idx == CurrentIndex) CurrentIndex = Math.Min(CurrentIndex, Items.Count - 1);
        if (Items.Count == 0) { IsExplicitQueue = false; CurrentIndex = -1; }
        NotifyCollectionDerived();
    }

    [RelayCommand]
    private void MoveUp(QueueItemViewModel? item)
    {
        if (item is null) return;
        var idx = Items.IndexOf(item);
        if (idx <= 0) return;
        Items.Move(idx, idx - 1);
        if (CurrentIndex == idx) CurrentIndex--;
        else if (CurrentIndex == idx - 1) CurrentIndex++;
        NotifyCollectionDerived();
    }

    [RelayCommand]
    private void MoveDown(QueueItemViewModel? item)
    {
        if (item is null) return;
        var idx = Items.IndexOf(item);
        if (idx < 0 || idx >= Items.Count - 1) return;
        Items.Move(idx, idx + 1);
        if (CurrentIndex == idx) CurrentIndex++;
        else if (CurrentIndex == idx + 1) CurrentIndex--;
        NotifyCollectionDerived();
    }

    [RelayCommand]
    private void Clear()
    {
        Items.Clear();
        CurrentIndex = -1;
        IsExplicitQueue = false;
        IsQueueOpen = false;
        NotifyCollectionDerived();
    }

    [RelayCommand]
    private void ToggleDrawer() => IsQueueOpen = !IsQueueOpen;
}
