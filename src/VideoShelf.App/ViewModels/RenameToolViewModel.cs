// src/VideoShelf.App/ViewModels/RenameToolViewModel.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoShelf.App.Motion;
using VideoShelf.App.Services;
using VideoShelf.Core.Models;
using VideoShelf.Core.Renaming;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.ViewModels;

/// <summary>Per-series opt-in rename tool: preview canonical (editable) names, confirm, defensive crash-safe
/// rename with an undo manifest, and one-click undo. The only feature that mutates video files.</summary>
public sealed partial class RenameToolViewModel : ObservableObject
{
    private const string LastManifestKey = "last_rename_manifest";

    private readonly LibraryRepository _library;
    private readonly RenamePlanner _planner;
    private readonly RenameExecutor _executor;
    private readonly SettingsRepository _settings;
    private readonly string _manifestDirectory;
    private readonly IToastService? _toasts;

    private long _seriesId;
    private bool _isStandalone;
    private string _baseTitle = "";
    private IReadOnlyList<Video> _videos = Array.Empty<Video>();
    private bool _suppressReplan;

    public RenameToolViewModel(
        LibraryRepository library,
        RenamePlanner planner,
        RenameExecutor executor,
        SettingsRepository settings,
        AppPaths paths,
        IToastService? toasts = null)
    {
        _library = library;
        _planner = planner;
        _executor = executor;
        _settings = settings;
        _manifestDirectory = paths.RenameManifestDirectory;
        _toasts = toasts;
    }

    public ObservableCollection<RenameRowViewModel> Rows { get; } = new();

    [ObservableProperty] private string _seriesTitle = "";
    [ObservableProperty] private string _statusSummary = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _canUndo;

    public event EventHandler? CloseRequested;

    public async Task LoadAsync(long seriesId, string baseTitle, bool isStandalone)
    {
        _seriesId = seriesId;
        _baseTitle = baseTitle;
        _isStandalone = isStandalone;
        SeriesTitle = baseTitle;

        // Single-file rename: load only the first video in the series.
        // For standalones this is the one file; for multi-episode series the user
        // renames one file at a time via the episode-level "Rename…" action.
        var allVideos = await Task.Run(() => _library.GetVideosForSeries(seriesId));
        _videos = allVideos.Count > 0 ? new[] { allVideos[0] } : Array.Empty<Video>();

        _suppressReplan = true;
        Rows.Clear();
        foreach (var v in _videos)
        {
            var ext = Path.GetExtension(v.FilePath);
            // Single-file: always use the standalone form (no episode number suffix).
            var proposed = CanonicalNamer.Build(_baseTitle, null, ext, padWidth: 0);
            var row = new RenameRowViewModel(v.Id, v.EpisodeNo, Path.GetFileName(v.FilePath), proposed, RenameItemStatus.Ready);
            row.NewNameEdited += (_, _) => Replan();
            Rows.Add(row);
        }
        _suppressReplan = false;

        Replan();
        CanUndo = _settings.GetString(LastManifestKey, "").Length > 0;
    }

    private void Replan()
    {
        if (_suppressReplan) return;
        var proposed = Rows.ToDictionary(r => r.VideoId, r => r.NewName);
        var plan = _planner.BuildPlan(_videos, proposed);
        var byId = plan.Items.ToDictionary(i => i.VideoId, i => i.Status);
        foreach (var row in Rows)
            if (byId.TryGetValue(row.VideoId, out var status))
                row.Status = status;

        var ready = plan.ReadyCount;
        var blocked = Rows.Count(r => r.Status is RenameItemStatus.TargetExists
            or RenameItemStatus.DuplicateTarget or RenameItemStatus.SourceMissing or RenameItemStatus.InvalidName);
        StatusSummary = blocked > 0 ? $"{ready} to rename, {blocked} blocked" : $"{ready} to rename";
        ApplyCommand.NotifyCanExecuteChanged();
    }

    private bool CanApply() => !IsBusy && Rows.Any(r => r.WillRename);

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task Apply()
    {
        IsBusy = true;
        try
        {
            var proposed = Rows.ToDictionary(r => r.VideoId, r => r.NewName);
            var plan = _planner.BuildPlan(_videos, proposed);
            var result = await Task.Run(() => _executor.Apply(plan, _seriesId, _manifestDirectory));

            if (result.ManifestPath is not null)
                _settings.SetString(LastManifestKey, result.ManifestPath);

            StatusSummary = result.Errors.Count > 0
                ? $"Renamed {result.Renamed}; {result.Errors.Count} error(s)"
                : $"Renamed {result.Renamed} file(s)";

            if (result.ManifestPath is not null)
                _toasts?.Show($"Renamed {result.Renamed} file(s)", undo: () => UndoCommand.Execute(null));

            await LoadAsync(_seriesId, _baseTitle, _isStandalone); // reflect disk truth
        }
        finally { IsBusy = false; }
    }

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
            StatusSummary = $"Reverted {result.Renamed} file(s)";
            await LoadAsync(_seriesId, _baseTitle, _isStandalone);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    partial void OnCanUndoChanged(bool value) => UndoCommand.NotifyCanExecuteChanged();

    partial void OnIsBusyChanged(bool value)
    {
        ApplyCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();
    }
}
