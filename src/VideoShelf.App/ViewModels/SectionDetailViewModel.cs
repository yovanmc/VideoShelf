using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoShelf.App.Motion;
using VideoShelf.App.Services;
using VideoShelf.Core.Models;
using VideoShelf.Core.Renaming;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.ViewModels;

public sealed partial class SectionDetailViewModel : ObservableObject, IBulkSelectionSource
{
    private readonly LibraryRepository library;
    private readonly TagRepository tags;
    private readonly WatchRepository watch;
    private readonly IThumbnailService thumbnails;
    private readonly CreatorArtRepository art;
    private readonly IImagePicker imagePicker;
    private readonly PlayQueueViewModel playQueue;
    private readonly CurationRepository? curation;
    private readonly PlaylistRepository? playlists;
    private readonly ItemArtRepository? itemArt;

    // ── M18-G: duplicate-resolve deps (nullable trailing params) ─────────────
    private readonly MaintenanceRepository? _maintenance;
    private readonly IRecycleBinService? _recycleBin;
    private readonly IConfirmService? _confirm;
    private readonly IFileSystem? _fs;

    // ── M21-B: toast service (nullable trailing param) ────────────────────────
    private readonly IToastService? _toasts;

    // ── D5: bulk-watched suppression flag ────────────────────────────────────
    // Set to true around bulk Refresh() loops so per-series SeriesCompleted toasts
    // are not raised for every series that transitions to 0 during a bulk admin action.
    private bool _suppressCelebration;

    // ── M18-H: grouping override edit (nullable trailing param) ──────────────
    /// <summary>
    /// Grouping-override operations for the Edit-mode affordances.
    /// Null when the caller does not supply it (e.g. lightweight test fixtures).
    /// </summary>
    public GroupingEditViewModel? GroupingEdit { get; private set; }

    // ── ICollectionView for live series filtering ─────────────────────────────
    // Null in test environments (no WPF Dispatcher).
    private readonly ICollectionView? _seriesView;

    public SectionDetailViewModel(
        LibraryRepository library,
        TagRepository tags,
        WatchRepository watch,
        IThumbnailService thumbnails,
        CreatorArtRepository art,
        IImagePicker imagePicker,
        PlayQueueViewModel playQueue,
        CurationRepository? curation = null,
        PlaylistRepository? playlists = null,
        ItemArtRepository? itemArt = null,
        MaintenanceRepository? maintenance = null,
        IRecycleBinService? recycleBin = null,
        IConfirmService? confirm = null,
        IFileSystem? fs = null,
        GroupingEditViewModel? groupingEdit = null,
        IToastService? toasts = null)
    {
        this.library      = library;
        this.tags         = tags;
        this.watch        = watch;
        this.thumbnails   = thumbnails;
        this.art          = art;
        this.imagePicker  = imagePicker;
        this.playQueue    = playQueue;
        this.curation     = curation;
        this.playlists    = playlists;
        this.itemArt      = itemArt;
        _maintenance      = maintenance;
        _recycleBin       = recycleBin;
        _confirm          = confirm;
        _fs               = fs;
        GroupingEdit      = groupingEdit;
        _toasts           = toasts;

        // Only attach the ICollectionView when a WPF Dispatcher is available.
        // In unit tests there is no Application/Dispatcher, and SeriesList is
        // mutated from background threads in LoadAsync, which would crash the view.
        if (System.Windows.Application.Current is not null)
        {
            _seriesView = CollectionViewSource.GetDefaultView(SeriesList);
            _seriesView.Filter = SeriesFilterPredicate;
        }
    }
    /// <summary>Shared playlist references for "add to playlist" menus on episode rows.</summary>
    public ObservableCollection<PlaylistRef> AvailablePlaylists { get; } = [];
    public long SectionId { get; private set; }

    private readonly SelectionViewModel<EpisodeViewModel> _selection = new();

    /// <summary>Per-page selection state spanning all loaded episode rows across series.</summary>
    public SelectionViewModel<EpisodeViewModel> Selection => _selection;

    // ── IBulkSelectionSource ─────────────────────────────────────────────────
    bool IBulkSelectionSource.HasSelection => Selection.HasSelection;
    IReadOnlyList<long> IBulkSelectionSource.GetSelectedVideoIds() => GetSelectedVideoIds();
    public event EventHandler? SelectionChanged;
    void IBulkSelectionSource.ClearSelection() => Selection.ClearSelectionCommand.Execute(null);
    void IBulkSelectionSource.ExitSelectionMode() => Selection.ExitSelectionModeCommand.Execute(null);

    /// <summary>Returns video ids for all currently selected episode rows.</summary>
    public IReadOnlyList<long> GetSelectedVideoIds()
        => Selection.SelectedItems.Select(e => e.VideoId).ToList();

    [ObservableProperty]
    private bool _isEditing;

    [RelayCommand]
    private void ToggleEdit() => IsEditing = !IsEditing;

    // ── Series filter bar (F1) ────────────────────────────────────────────────

    [ObservableProperty]
    private string _seriesFilterText = "";

    [ObservableProperty]
    private bool _isSeriesFilterVisible;

    [RelayCommand]
    private void ToggleSeriesFilter()
    {
        IsSeriesFilterVisible = !IsSeriesFilterVisible;
        if (!IsSeriesFilterVisible)
            SeriesFilterText = "";
    }

    [RelayCommand]
    private void ClearSeriesFilter() => SeriesFilterText = "";

    partial void OnSeriesFilterTextChanged(string value) => _seriesView?.Refresh();

    /// <summary>
    /// Pure filter predicate — unit-testable directly.
    /// Matches series base title case-insensitively against the filter text.
    /// </summary>
    public static bool SeriesMatchesPredicate(SeriesViewModel series, string filterText)
    {
        if (string.IsNullOrWhiteSpace(filterText)) return true;
        return series.BaseTitle.Contains(filterText, StringComparison.OrdinalIgnoreCase);
    }

    private bool SeriesFilterPredicate(object item)
        => item is SeriesViewModel s && SeriesMatchesPredicate(s, SeriesFilterText);

    [RelayCommand]
    private void PlayAll() => playQueue.PlayAll(library.GetEpisodesForSection(SectionId));

    [RelayCommand]
    private void MarkCreatorWatched()
    {
        watch.SetWatchedForSection(SectionId, true);
        // D5: suppress per-series celebration toasts during bulk admin action.
        _suppressCelebration = true;
        try
        {
            foreach (var s in SeriesList) s.Refresh();
        }
        finally
        {
            _suppressCelebration = false;
        }
    }

    [RelayCommand]
    private void MarkCreatorUnwatched()
    {
        watch.SetWatchedForSection(SectionId, false);
        foreach (var s in SeriesList) s.Refresh();
    }

    /// <summary>
    /// Collapses all series tiles. Immediate — no lazy-load triggered.
    /// </summary>
    [RelayCommand]
    private void CollapseAll()
    {
        foreach (var s in SeriesList)
            s.IsExpanded = false;
    }

    /// <summary>
    /// Expands all series tiles and triggers lazy episode loading for each.
    /// All series are expanded concurrently: already-loaded series short-circuit
    /// immediately (no DB round-trip); unloaded ones fan out as parallel
    /// <c>Task.Run</c>-backed reads — all off the UI thread, so no stall.
    /// Standalone series are skipped by <see cref="SeriesViewModel.ExpandAsync"/>.
    /// </summary>
    [RelayCommand]
    private async Task ExpandAll()
    {
        // Fan out concurrently; each ExpandAsync uses Task.Run internally so
        // the UI thread is not blocked even at 40+ series.
        var tasks = SeriesList.Select(s => s.ExpandAsync());
        await Task.WhenAll(tasks);
    }

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

    // ── M18-G: possible duplicates banner ─────────────────────────────────────

    /// <summary>Raised when the owner wants to open the resolve screen for a group.</summary>
    public event EventHandler<DuplicateResolveViewModel>? ResolveRequested;

    /// <summary>Duplicate groups scoped to this creator section. Populated on LoadAsync.</summary>
    public ObservableCollection<DuplicateGroup> PossibleDuplicates { get; } = new();

    /// <summary>True when <see cref="PossibleDuplicates"/> has entries.</summary>
    public bool HasDuplicates => PossibleDuplicates.Count > 0;

    /// <summary>Opens the compare/resolve screen for the given group.</summary>
    [RelayCommand]
    private void OpenResolve(DuplicateGroup group)
    {
        if (_maintenance is null || _recycleBin is null || _confirm is null || _fs is null) return;
        var vm = new DuplicateResolveViewModel(group, _maintenance, library, _recycleBin, _confirm, _fs);
        vm.Resolved += (_, _) => RefreshDuplicates();
        ResolveRequested?.Invoke(this, vm);
    }

    private void RefreshDuplicates()
    {
        PossibleDuplicates.Clear();
        if (_maintenance is not null)
            foreach (var g in _maintenance.GetDuplicateGroupsForSection(SectionId))
                PossibleDuplicates.Add(g);
        OnPropertyChanged(nameof(HasDuplicates));
    }

    // ── M18-H: regroup callback ───────────────────────────────────────────────

    /// <summary>
    /// Called when <see cref="GroupingEdit"/> fires <c>RegroupRequested</c>.
    /// Reloads the full page so the new series/episode layout is reflected.
    /// Fire-and-forget: we are on the UI thread (event handler), so we use
    /// <c>_ = LoadAsync(SectionId)</c> to kick off the task without blocking.
    /// </summary>
    private void OnRegroupRequested(object? sender, EventArgs e)
        => _ = LoadAsync(SectionId);

    // ── M18-H: pass-through commands for grouping override UI ────────────────

    /// <summary>
    /// Clears the grouping override for a single episode (by its file path) and regroups.
    /// Bound in XAML via CommandParameter={Binding FilePath}.
    /// </summary>
    [RelayCommand]
    private void ResetEpisodeGrouping(string filePath)
        => GroupingEdit?.ResetEpisodeGroupingCommand.Execute(filePath);

    /// <summary>
    /// Clears all grouping overrides for every episode in the given series and regroups.
    /// Bound in XAML via CommandParameter={Binding} (passes the SeriesViewModel).
    /// </summary>
    [RelayCommand]
    private void ResetSeriesGrouping(SeriesViewModel? series)
    {
        if (GroupingEdit is null || series is null) return;
        // Collect file paths from loaded episodes; also query DB for those not yet loaded.
        var filePaths = library.GetEpisodes(series.SeriesId)
                               .Select(e => e.FilePath)
                               .ToList();
        GroupingEdit.ResetSeriesGroupingCommand.Execute(filePaths);
    }

    /// <summary>
    /// Moves an episode to a target series by setting <c>override_base_title</c>.
    /// CommandParameter is the <see cref="EpisodeViewModel"/>; the target series title
    /// is supplied by <see cref="MoveEpisodeTargetTitle"/>.
    /// </summary>
    [RelayCommand]
    private void MoveEpisodeToSeries(EpisodeViewModel? episode)
    {
        if (GroupingEdit is null || episode is null) return;
        var target = MoveEpisodeTargetTitle.Trim();
        if (target.Length == 0) return;
        GroupingEdit.MoveEpisodeToSeriesCommand.Execute(new MoveEpisodeArgs(episode.FilePath, target));
        MoveEpisodeTargetTitle = "";
    }

    /// <summary>Transient input field for the "Move to series…" title.</summary>
    [ObservableProperty]
    private string _moveEpisodeTargetTitle = "";

    /// <summary>
    /// Merges all episodes of a series into another series whose title is given by
    /// <see cref="MergeTargetTitle"/>.
    /// CommandParameter is the <see cref="SeriesViewModel"/> being merged away.
    /// </summary>
    [RelayCommand]
    private void MergeSeriesInto(SeriesViewModel? series)
    {
        if (GroupingEdit is null || series is null) return;
        var target = MergeTargetTitle.Trim();
        if (target.Length == 0) return;
        var filePaths = library.GetEpisodes(series.SeriesId)
                               .Select(e => e.FilePath)
                               .ToList();
        GroupingEdit.MergeIntoSeriesCommand.Execute(new MergeSeriesArgs(filePaths, target));
        MergeTargetTitle = "";
    }

    /// <summary>Transient input field for the "Merge into…" target series title.</summary>
    [ObservableProperty]
    private string _mergeTargetTitle = "";

    public async Task LoadAsync(long sectionId)
    {
        SectionId = sectionId;
        IsEditing = false;
        // Clear ephemeral filter when navigating to a new creator page.
        SeriesFilterText = "";
        IsSeriesFilterVisible = false;

        // M18-H: attach the grouping-edit VM so its commands know the section.
        if (GroupingEdit is not null)
        {
            GroupingEdit.Attach(sectionId);
            // Reload this page whenever a regroup completes.
            GroupingEdit.RegroupRequested -= OnRegroupRequested;
            GroupingEdit.RegroupRequested += OnRegroupRequested;
        }

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

        // Unsubscribe from existing series episodes before clearing.
        foreach (var existing in SeriesList)
            existing.Episodes.CollectionChanged -= OnSeriesEpisodesChanged;

        Selection.ExitSelectionModeCommand.Execute(null);

        SeriesList.Clear();
        foreach (var s in summaries)
        {
            var svm = new SeriesViewModel(s, library, watch, thumbnails, tags, curation, playlists, AvailablePlaylists, itemArt, imagePicker, _toasts);
            svm.PlayRequested += (_, e) => PlayRequested?.Invoke(this, e);
            svm.RenameRequested += (_, sv) => RenameRequested?.Invoke(this, sv);
            svm.PlayAllRequested += (_, _) => playQueue.PlayAll(library.GetEpisodes(svm.SeriesId));
            svm.EnqueueRequested += (_, _) => playQueue.EnqueueRange(library.GetEpisodes(svm.SeriesId));
            svm.PlayNextRequested += (_, _) => playQueue.PlayNextRange(library.GetEpisodes(svm.SeriesId));
            svm.MarkWatchedRequested += (_, _) => { watch.SetWatchedForSeries(svm.SeriesId, true); svm.Refresh(); };
            svm.MarkUnwatchedRequested += (_, _) => { watch.SetWatchedForSeries(svm.SeriesId, false); svm.Refresh(); };
            // D5: celebrate when all episodes in a series become watched.
            // Skipped during bulk-mark-watched operations (_suppressCelebration=true).
            svm.SeriesCompleted += (_, _) =>
            {
                if (!_suppressCelebration)
                    _toasts?.Show($"🎉 Finished {svm.BaseTitle}!", kind: ToastKind.Success);
            };
            // Subscribe to episodes as they lazy-load so each episode feeds the section-level Selection.
            svm.Episodes.CollectionChanged += OnSeriesEpisodesChanged;
            SeriesList.Add(svm);
            _ = svm.LoadThumbnailAsync(CancellationToken.None);   // eager tile art (cached + fail-safe)
        }

        Tags.Clear();
        foreach (var t in sectionTags) Tags.Add(t);
        RefreshSuggestions();
        RefreshCreatorArt();                 // existing: sets CreatorArtPath from the override
        RefreshDuplicates();                 // M18-G: populate possible-duplicates banner
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

    // ── Episode selection wiring ─────────────────────────────────────────────

    /// <summary>
    /// Called when episodes are added to any series in this section.
    /// Subscribes each new episode's PropertyChanged to route IsSelected changes
    /// into the section-level <see cref="Selection"/> — no back-ref in the episode.
    /// </summary>
    private void OnSeriesEpisodesChanged(object? sender,
        System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (EpisodeViewModel ep in e.NewItems)
                ep.PropertyChanged += OnEpisodePropertyChanged;
        }
        if (e.OldItems is not null)
        {
            foreach (EpisodeViewModel ep in e.OldItems)
                ep.PropertyChanged -= OnEpisodePropertyChanged;
        }
    }

    private void OnEpisodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EpisodeViewModel.IsSelected) &&
            sender is EpisodeViewModel ep)
        {
            Selection.OnItemSelectionChanged(ep);
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
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
