// src/VideoShelf.App/ViewModels/MultiRenameViewModel.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoShelf.App.Services;
using VideoShelf.Core.Models;
using VideoShelf.Core.Renaming;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.ViewModels;

/// <summary>
/// Cross-series template rename tool. Spans a selection of series, building ONE combined
/// <see cref="RenamePlan"/> and writing ONE undo manifest so the entire batch is atomically reversible.
/// <para>
/// Default template: <c>"{creator} - {series} - {NN}"</c> (produces names that re-parse stably via
/// <c>TitleParser</c> to a deterministic title + episode pair). Single-series rename is a special
/// case: seeding with exactly one series id gives the same result as using
/// <c>RenameToolViewModel</c> with that template.
/// </para>
/// </summary>
public sealed partial class MultiRenameViewModel : ObservableObject
{
    /// <summary>Default template exposed for the entry-point button and for tests.</summary>
    public const string DefaultTemplate = "{creator} - {series} - {NN}";

    private const string LastManifestKey = "last_rename_manifest";

    private readonly LibraryRepository _library;
    private readonly RenamePlanner _planner;
    private readonly RenameExecutor _executor;
    private readonly SettingsRepository _settings;
    private readonly string _manifestDirectory;

    // Full video list across all series (ordered: series then by episode_no within each).
    private IReadOnlyList<Video> _allVideos = Array.Empty<Video>();
    private bool _suppressReplan;

    public MultiRenameViewModel(
        LibraryRepository library,
        RenamePlanner planner,
        RenameExecutor executor,
        SettingsRepository settings,
        AppPaths paths)
    {
        _library = library;
        _planner = planner;
        _executor = executor;
        _settings = settings;
        _manifestDirectory = paths.RenameManifestDirectory;
    }

    // ── State ──────────────────────────────────────────────────────────────────

    public ObservableCollection<RenameRowViewModel> Rows { get; } = new();

    [ObservableProperty] private string _template = DefaultTemplate;
    [ObservableProperty] private string _statusSummary = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _canUndo;

    /// <summary>Raised when the view should close (navigate back).</summary>
    public event EventHandler? CloseRequested;

    // ── Load ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads all videos across the supplied series ids, renders proposed names with
    /// <paramref name="template"/>, and builds the initial combined plan.
    /// </summary>
    public async Task LoadAsync(IReadOnlyList<long> seriesIds, string template)
    {
        // Suppress the partial OnTemplateChanged handler while loading rows from scratch.
        _suppressReplan = true;
        Template = template;
        _suppressReplan = false;

        // Resolve (series, creator) pairs + fetch videos — all off the UI thread.
        var (videosBySeries, contextBySeries) = await Task.Run(() => ResolveAllSeries(seriesIds));

        _allVideos = videosBySeries.Values.SelectMany(v => v).ToList();

        var padWidth = CanonicalNamer.PadWidth(_allVideos.Select(v => v.EpisodeNo));

        _suppressReplan = true;
        Rows.Clear();
        foreach (var seriesId in seriesIds)
        {
            if (!videosBySeries.TryGetValue(seriesId, out var videos)) continue;
            if (!contextBySeries.TryGetValue(seriesId, out var ctx)) continue;

            foreach (var v in videos)
            {
                var ext = Path.GetExtension(v.FilePath);
                var proposed = CanonicalNamer.RenderTemplate(template, ctx, v.EpisodeNo, ext, padWidth);
                var row = new RenameRowViewModel(v.Id, v.EpisodeNo, Path.GetFileName(v.FilePath), proposed, RenameItemStatus.Ready);
                row.NewNameEdited += (_, _) => Replan();
                Rows.Add(row);
            }
        }
        _suppressReplan = false;

        Replan();
        CanUndo = _settings.GetString(LastManifestKey, "").Length > 0;
    }

    // ── Re-render on template change ──────────────────────────────────────────

    partial void OnTemplateChanged(string value) => ReplanWithNewTemplate();

    private void ReplanWithNewTemplate()
    {
        if (_suppressReplan || Rows.Count == 0) return;

        // Re-render all rows with the new template (but honour any per-row overrides already in
        // effect by only re-rendering rows whose current NewName still matches the OLD template output).
        // Simple approach: re-render all rows — the user can then override individual cells.
        // This matches how single-series RenameToolViewModel reloads on every plan change.
        var padWidth = CanonicalNamer.PadWidth(_allVideos.Select(v => v.EpisodeNo));

        // Build a lookup from VideoId → Video for template rendering.
        var byId = _allVideos.ToDictionary(v => v.Id);

        // Build a lookup from VideoId → TemplateContext.
        // We rely on the contextBySeries that was resolved at Load time — we need to store it.
        // Since we don't store it, we re-resolve from the rows via video → series → section.
        // To avoid re-hitting the DB excessively, build the dict once here.
        var ctxByVideoId = BuildContextByVideoId();

        _suppressReplan = true;
        foreach (var row in Rows)
        {
            if (!byId.TryGetValue(row.VideoId, out var video)) continue;
            if (!ctxByVideoId.TryGetValue(row.VideoId, out var ctx)) continue;
            var ext = Path.GetExtension(video.FilePath);
            row.NewName = CanonicalNamer.RenderTemplate(Template, ctx, video.EpisodeNo, ext, padWidth);
        }
        _suppressReplan = false;

        Replan();
    }

    // ── Replan ───────────────────────────────────────────────────────────────

    private void Replan()
    {
        if (_suppressReplan) return;
        var proposed = Rows.ToDictionary(r => r.VideoId, r => r.NewName);
        var plan = _planner.BuildPlan(_allVideos, proposed);
        var byId = plan.Items.ToDictionary(i => i.VideoId, i => i.Status);
        foreach (var row in Rows)
            if (byId.TryGetValue(row.VideoId, out var status))
                row.Status = status;

        var ready = plan.ReadyCount;
        var blocked = Rows.Count(r => r.Status is RenameItemStatus.TargetExists
            or RenameItemStatus.DuplicateTarget or RenameItemStatus.SourceMissing or RenameItemStatus.InvalidName);
        var seriesCount = Rows.Select(r => GetSeriesIdForVideoId(r.VideoId)).Distinct().Count();
        StatusSummary = blocked > 0
            ? $"{ready} to rename across {seriesCount} series, {blocked} blocked"
            : $"{ready} to rename across {seriesCount} series";
        ApplyCommand.NotifyCanExecuteChanged();
    }

    // ── Apply ─────────────────────────────────────────────────────────────────

    private bool CanApply() => !IsBusy && Rows.Any(r => r.WillRename);

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task Apply()
    {
        IsBusy = true;
        try
        {
            var proposed = Rows.ToDictionary(r => r.VideoId, r => r.NewName);
            var plan = _planner.BuildPlan(_allVideos, proposed);

            // ONE manifest for the entire multi-series batch — this is the key safety guarantee.
            var result = await Task.Run(() => _executor.Apply(plan, _manifestDirectory));

            if (result.ManifestPath is not null)
                _settings.SetString(LastManifestKey, result.ManifestPath);

            StatusSummary = result.Errors.Count > 0
                ? $"Renamed {result.Renamed}; {result.Errors.Count} error(s)"
                : $"Renamed {result.Renamed} file(s)";

            CanUndo = result.ManifestPath is not null;
        }
        finally { IsBusy = false; }
    }

    // ── Undo ──────────────────────────────────────────────────────────────────

    private bool CanRunUndo() => !IsBusy && CanUndo;

    [RelayCommand(CanExecute = nameof(CanRunUndo))]
    private async Task Undo()
    {
        var manifestPath = _settings.GetString(LastManifestKey, "");
        if (manifestPath.Length == 0) return;

        IsBusy = true;
        try
        {
            var result = await Task.Run(() => _executor.Undo(manifestPath));
            _settings.SetString(LastManifestKey, ""); // consumed
            CanUndo = false;
            StatusSummary = $"Reverted {result.Renamed} file(s)";
        }
        finally { IsBusy = false; }
    }

    // ── Close ─────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    // ── Partial changed ───────────────────────────────────────────────────────

    partial void OnCanUndoChanged(bool value) => UndoCommand.NotifyCanExecuteChanged();

    partial void OnIsBusyChanged(bool value)
    {
        ApplyCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Resolves all series ids into (videos, templateContext) maps. Pure DB work; call off UI thread.
    /// </summary>
    private (Dictionary<long, IReadOnlyList<Video>> VideosBySeries,
             Dictionary<long, TemplateContext> ContextBySeries)
        ResolveAllSeries(IReadOnlyList<long> seriesIds)
    {
        var videosBySeries = new Dictionary<long, IReadOnlyList<Video>>();
        var contextBySeries = new Dictionary<long, TemplateContext>();
        // Cache section lookups to avoid N round-trips when many series share one creator.
        var sectionCache = new Dictionary<long, string>();

        foreach (var seriesId in seriesIds)
        {
            var series = _library.GetSeries(seriesId);
            if (series is null) continue;

            if (!sectionCache.TryGetValue(series.SectionId, out var creatorName))
            {
                var section = _library.GetSection(series.SectionId);
                creatorName = section?.DisplayName ?? "";
                sectionCache[series.SectionId] = creatorName;
            }

            var videos = _library.GetVideosForSeries(seriesId);
            videosBySeries[seriesId] = videos;
            contextBySeries[seriesId] = new TemplateContext(creatorName, series.BaseTitle);
        }

        return (videosBySeries, contextBySeries);
    }

    // videoId → seriesId mapping (built lazily from _allVideos and the DB).
    // We store videos with their series_id from the Video model.
    private long GetSeriesIdForVideoId(long videoId)
    {
        foreach (var v in _allVideos)
            if (v.Id == videoId) return v.SeriesId;
        return 0;
    }

    /// <summary>
    /// Builds a VideoId → TemplateContext map so ReplanWithNewTemplate can re-render without
    /// re-resolving from the DB. We walk _allVideos (which carry SeriesId) and join against
    /// the section cache already available in memory.
    /// </summary>
    private Dictionary<long, TemplateContext> BuildContextByVideoId()
    {
        // Resolve section→creatorName for all unique section IDs present in the current video set.
        var sectionCache = new Dictionary<long, string>();
        // series→(sectionId, baseTitle) cache
        var seriesCache = new Dictionary<long, (long SectionId, string BaseTitle)>();

        foreach (var v in _allVideos)
        {
            if (!seriesCache.ContainsKey(v.SeriesId))
            {
                var series = _library.GetSeries(v.SeriesId);
                if (series is null) continue;
                seriesCache[v.SeriesId] = (series.SectionId, series.BaseTitle);
                if (!sectionCache.ContainsKey(series.SectionId))
                {
                    var section = _library.GetSection(series.SectionId);
                    sectionCache[series.SectionId] = section?.DisplayName ?? "";
                }
            }
        }

        var result = new Dictionary<long, TemplateContext>();
        foreach (var v in _allVideos)
        {
            if (!seriesCache.TryGetValue(v.SeriesId, out var si)) continue;
            if (!sectionCache.TryGetValue(si.SectionId, out var creatorName)) continue;
            result[v.Id] = new TemplateContext(creatorName, si.BaseTitle);
        }
        return result;
    }
}
