using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoShelf.App.Services;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.ViewModels;

public sealed partial class SectionDetailViewModel(
    LibraryRepository library,
    TagRepository tags,
    WatchRepository watch,
    IThumbnailService thumbnails) : ObservableObject
{
    public long SectionId { get; private set; }

    [ObservableProperty] private string _displayName = "";
    [ObservableProperty] private string _tagInput = "";

    private IReadOnlyList<string> _allTags = [];

    public ObservableCollection<SeriesViewModel> SeriesList { get; } = [];
    public ObservableCollection<string> Tags { get; } = [];
    public ObservableCollection<string> Suggestions { get; } = [];

    public event EventHandler<EpisodeView>? PlayRequested;

    public async Task LoadAsync(long sectionId)
    {
        SectionId = sectionId;

        var section = library.GetSection(sectionId);
        DisplayName = section?.DisplayName ?? "";

        var (summaries, sectionTags, allTags) = await Task.Run(() => (
            library.GetSeriesSummaries(sectionId),
            tags.GetTags(sectionId),
            tags.GetAllTags()));
        _allTags = allTags;

        SeriesList.Clear();
        foreach (var s in summaries)
        {
            var svm = new SeriesViewModel(s, library, watch, thumbnails);
            svm.PlayRequested += (_, e) => PlayRequested?.Invoke(this, e);
            SeriesList.Add(svm);
        }

        Tags.Clear();
        foreach (var t in sectionTags) Tags.Add(t);
        RefreshSuggestions();
    }

    [RelayCommand]
    private void AddTag() => DoAddTag();

    [RelayCommand]
    private void AddSuggestion(string tag)
    {
        TagInput = tag;
        DoAddTag();
    }

    private void DoAddTag()
    {
        var norm = TagRepository.Normalize(TagInput);
        if (norm.Length == 0) return;
        tags.AddTag(SectionId, norm);
        if (!Tags.Contains(norm)) Tags.Add(norm);
        // Keep the cache consistent: if this is a brand-new tag, append it.
        if (!_allTags.Contains(norm))
            _allTags = [.. _allTags, norm];
        TagInput = "";
        RefreshSuggestions();
    }

    [RelayCommand]
    private void RemoveTag(string tag)
    {
        tags.RemoveTag(SectionId, tag);
        Tags.Remove(tag);
        RefreshSuggestions();
    }

    partial void OnTagInputChanged(string value) => RefreshSuggestions();

    private void RefreshSuggestions()
    {
        var query = TagRepository.Normalize(TagInput);
        var applied = new HashSet<string>(Tags);
        Suggestions.Clear();
        foreach (var t in _allTags)
        {
            if (applied.Contains(t)) continue;
            if (query.Length > 0 && !t.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
            Suggestions.Add(t);
        }
    }
}
