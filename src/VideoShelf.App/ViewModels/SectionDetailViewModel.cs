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

    public ObservableCollection<SeriesViewModel> SeriesList { get; } = [];
    public ObservableCollection<string> Tags { get; } = [];
    public ObservableCollection<string> Suggestions { get; } = [];

    public event EventHandler<EpisodeView>? PlayRequested;

    public async Task LoadAsync(long sectionId)
    {
        SectionId = sectionId;

        var section = library.GetSection(sectionId);
        DisplayName = section?.DisplayName ?? "";

        var summaries = await Task.Run(() => library.GetSeriesSummaries(sectionId));
        SeriesList.Clear();
        foreach (var s in summaries)
        {
            var svm = new SeriesViewModel(s, library, watch, thumbnails);
            svm.PlayRequested += (_, e) => PlayRequested?.Invoke(this, e);
            SeriesList.Add(svm);
        }

        Tags.Clear();
        foreach (var t in await Task.Run(() => tags.GetTags(sectionId))) Tags.Add(t);
        RefreshSuggestions();
    }

    [RelayCommand]
    private void AddTag()
    {
        var norm = TagRepository.Normalize(TagInput);
        if (norm.Length == 0) return;
        tags.AddTag(SectionId, norm);
        if (!Tags.Contains(norm)) Tags.Add(norm);
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
        var all = tags.GetAllTags();
        Suggestions.Clear();
        foreach (var t in all)
        {
            if (applied.Contains(t)) continue;
            if (query.Length > 0 && !t.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
            Suggestions.Add(t);
        }
    }
}
