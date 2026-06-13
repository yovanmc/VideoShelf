using System;
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

public sealed partial class SectionDetailViewModel(
    LibraryRepository library,
    TagRepository tags,
    WatchRepository watch,
    IThumbnailService thumbnails,
    CreatorArtRepository art,
    IImagePicker imagePicker,
    PlayQueueViewModel playQueue,
    CurationRepository? curation = null,
    PlaylistRepository? playlists = null) : ObservableObject
{
    /// <summary>Shared playlist references for "add to playlist" menus on episode rows.</summary>
    public ObservableCollection<PlaylistRef> AvailablePlaylists { get; } = [];
    public long SectionId { get; private set; }

    [ObservableProperty]
    private bool _isEditing;

    [RelayCommand]
    private void ToggleEdit() => IsEditing = !IsEditing;

    [RelayCommand]
    private void PlayAll() => playQueue.PlayAll(library.GetEpisodesForSection(SectionId));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCreatorArt))]
    private string? _creatorArtPath;

    public bool HasCreatorArt => !string.IsNullOrEmpty(CreatorArtPath);

    [ObservableProperty] private string _displayName = "";
    [ObservableProperty] private string _tagInput = "";

    [ObservableProperty] private string? _backgroundImagePath;
    [ObservableProperty] private int _videoCount;
    private string? _seedPath;   // section representative seed frame, for the background fallback

    private IReadOnlyList<string> _allTags = [];

    public ObservableCollection<SeriesViewModel> SeriesList { get; } = [];
    public ObservableCollection<string> Tags { get; } = [];
    public ObservableCollection<string> Suggestions { get; } = [];

    public event EventHandler<EpisodeView>? PlayRequested;
    public event EventHandler<SeriesViewModel>? RenameRequested;

    public async Task LoadAsync(long sectionId)
    {
        SectionId = sectionId;
        IsEditing = false;

        // GetSection(long) returns a lean Section without VideoCount/ThumbnailSeedPath;
        // use GetSectionSummaries().First(...) to get the full SectionSummary.
        var section = library.GetSectionSummaries().FirstOrDefault(s => s.SectionId == sectionId);
        DisplayName = section?.DisplayName ?? "";
        VideoCount = section?.VideoCount ?? 0;
        _seedPath = section?.ThumbnailSeedPath;

        var (summaries, sectionTags, allTags) = await Task.Run(() => (
            library.GetSeriesSummaries(sectionId),
            tags.GetTags(sectionId),
            tags.GetAllTags()));
        _allTags = allTags;

        // Refresh shared playlist list for "add to playlist" menus.
        AvailablePlaylists.Clear();
        if (playlists is not null)
            foreach (var p in playlists.GetAll())
                AvailablePlaylists.Add(new PlaylistRef(p.Id, p.Name));

        SeriesList.Clear();
        foreach (var s in summaries)
        {
            var svm = new SeriesViewModel(s, library, watch, thumbnails, tags, curation, playlists, AvailablePlaylists);
            svm.PlayRequested += (_, e) => PlayRequested?.Invoke(this, e);
            svm.RenameRequested += (_, sv) => RenameRequested?.Invoke(this, sv);
            svm.PlayAllRequested += (_, _) => playQueue.PlayAll(library.GetEpisodes(svm.SeriesId));
            svm.EnqueueRequested += (_, _) => playQueue.EnqueueRange(library.GetEpisodes(svm.SeriesId));
            svm.PlayNextRequested += (_, _) => playQueue.PlayNextRange(library.GetEpisodes(svm.SeriesId));
            SeriesList.Add(svm);
            _ = svm.LoadThumbnailAsync(CancellationToken.None);   // eager tile art (cached + fail-safe)
        }

        Tags.Clear();
        foreach (var t in sectionTags) Tags.Add(t);
        RefreshSuggestions();
        RefreshCreatorArt();                 // existing: sets CreatorArtPath from the override
        await ResolveBackgroundAsync();
    }

    private void RefreshCreatorArt() => CreatorArtPath = art.GetArtPath(SectionId);

    private async Task ResolveBackgroundAsync()
    {
        if (!string.IsNullOrWhiteSpace(CreatorArtPath)) { BackgroundImagePath = CreatorArtPath; return; }
        if (string.IsNullOrWhiteSpace(_seedPath)) { BackgroundImagePath = null; return; }
        BackgroundImagePath = await thumbnails.GetThumbnailPathAsync(_seedPath!, CancellationToken.None);
    }

    [RelayCommand]
    private async Task SetCreatorArt()
    {
        if (SectionId <= 0) return;
        var picked = imagePicker.PickImage();
        if (string.IsNullOrWhiteSpace(picked)) return;
        art.SetArtPath(SectionId, picked);
        CreatorArtPath = picked;
        await ResolveBackgroundAsync();
    }

    [RelayCommand]
    private async Task ClearCreatorArt()
    {
        if (SectionId <= 0) return;
        art.ClearArtPath(SectionId);
        CreatorArtPath = null;
        await ResolveBackgroundAsync();
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
