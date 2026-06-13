using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.ViewModels;

public enum TagLevel { Section, Series, Video }

public sealed record InheritedTagViewModel(string Tag, string SourceLabel);

public sealed partial class TagEditorViewModel(TagRepository tags) : ObservableObject
{
    public TagLevel Level { get; private set; }
    public long TargetId { get; private set; }

    public ObservableCollection<string> Tags { get; } = [];
    public ObservableCollection<InheritedTagViewModel> Inherited { get; } = [];
    public ObservableCollection<string> Suggestions { get; } = [];

    [ObservableProperty] private string _tagInput = "";

    public event System.Action? Changed;

    private IReadOnlyList<string> _allTags = [];

    public void Load(TagLevel level, long targetId)
    {
        Level = level;
        TargetId = targetId;

        Tags.Clear();
        Inherited.Clear();
        Suggestions.Clear();

        var applied = GetAppliedTags();
        foreach (var t in applied) Tags.Add(t);

        var appliedSet = new HashSet<string>(Tags);

        // Populate inherited (suppress entries already applied at this level)
        if (level == TagLevel.Series)
        {
            foreach (var t in tags.GetSectionTagsForSeries(targetId))
            {
                if (!appliedSet.Contains(t))
                    Inherited.Add(new InheritedTagViewModel(t, "from Creator"));
            }
        }
        else if (level == TagLevel.Video)
        {
            foreach (var t in tags.GetSeriesTagsForVideo(targetId))
            {
                if (!appliedSet.Contains(t))
                    Inherited.Add(new InheritedTagViewModel(t, "from Series"));
            }
            foreach (var t in tags.GetSectionTagsForVideo(targetId))
            {
                if (!appliedSet.Contains(t))
                    Inherited.Add(new InheritedTagViewModel(t, "from Creator"));
            }
        }

        _allTags = tags.GetAllTagsAcrossLevels();
        RefreshSuggestions();
    }

    private IReadOnlyList<string> GetAppliedTags() => Level switch
    {
        TagLevel.Section => tags.GetTags(TargetId),
        TagLevel.Series  => tags.GetSeriesTags(TargetId),
        TagLevel.Video   => tags.GetVideoTags(TargetId),
        _                => []
    };

    [RelayCommand]
    private void AddTag()
    {
        var norm = TagRepository.Normalize(TagInput);
        if (norm.Length == 0) return;

        switch (Level)
        {
            case TagLevel.Section: tags.AddTag(TargetId, norm); break;
            case TagLevel.Series:  tags.AddSeriesTag(TargetId, norm); break;
            case TagLevel.Video:   tags.AddVideoTag(TargetId, norm); break;
        }

        if (!Tags.Contains(norm)) Tags.Add(norm);

        // Keep cache consistent: append brand-new tags
        if (!_allTags.Contains(norm))
            _allTags = [.. _allTags, norm];

        TagInput = "";
        RefreshSuggestions();
        Changed?.Invoke();
    }

    [RelayCommand]
    private void AddSuggestion(string tag)
    {
        TagInput = tag;
        AddTag();
    }

    [RelayCommand]
    private void RemoveTag(string tag)
    {
        switch (Level)
        {
            case TagLevel.Section: tags.RemoveTag(TargetId, tag); break;
            case TagLevel.Series:  tags.RemoveSeriesTag(TargetId, tag); break;
            case TagLevel.Video:   tags.RemoveVideoTag(TargetId, tag); break;
        }

        Tags.Remove(tag);
        RefreshSuggestions();
        Changed?.Invoke();
    }

    partial void OnTagInputChanged(string value) => RefreshSuggestions();

    private void RefreshSuggestions()
    {
        var query = TagRepository.Normalize(TagInput);
        var appliedSet = new HashSet<string>(Tags);
        var inheritedSet = new HashSet<string>(Inherited.Select(i => i.Tag));

        Suggestions.Clear();
        foreach (var t in _allTags)
        {
            if (appliedSet.Contains(t)) continue;
            if (inheritedSet.Contains(t)) continue;
            if (query.Length > 0 && !t.Contains(query, System.StringComparison.OrdinalIgnoreCase)) continue;
            Suggestions.Add(t);
        }
    }
}
