using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoShelf.App.Services;
using VideoShelf.Core.Models;
using VideoShelf.Core.Scanning;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.ViewModels;

// ── Row VMs ───────────────────────────────────────────────────────────────────

/// <summary>A single row in the missing-file triage list.</summary>
public sealed partial class MissingVideoRow : ObservableObject
{
    private readonly MissingTriageViewModel _owner;

    public long VideoId { get; }
    public string FilePath { get; }
    public string CreatorName { get; }
    public string SeriesTitle { get; }

    /// <summary>Short display name: just the filename.</summary>
    public string FileName => Path.GetFileName(FilePath);

    /// <summary>Auto-find status: null = not run, empty = no candidate, else the candidate path.</summary>
    [ObservableProperty] private string? _autoFindResult;

    public MissingVideoRow(MissingVideo model, MissingTriageViewModel owner)
    {
        VideoId = model.Id;
        FilePath = model.FilePath;
        CreatorName = model.CreatorName;
        SeriesTitle = model.SeriesTitle;
        _owner = owner;
    }

    /// <summary>Opens a file picker so the owner can choose the new path manually.</summary>
    [RelayCommand]
    private void Relink() => _owner.RelinkManual(this);

    /// <summary>Runs auto-find by size_bytes match inside the source root.</summary>
    [RelayCommand]
    private void AutoFind() => _owner.AutoFind(this);
}

/// <summary>A single orphan-series row (series with zero playable videos).</summary>
public sealed partial class OrphanSeriesRow : ObservableObject
{
    private readonly MissingTriageViewModel _owner;

    public long SeriesId { get; }
    public string Title { get; }
    public string CreatorName { get; }

    public OrphanSeriesRow(OrphanEntry entry, MissingTriageViewModel owner)
    {
        SeriesId = entry.Id;
        Title = entry.Title;
        CreatorName = entry.CreatorName;
        _owner = owner;
    }

    /// <summary>Removes the series from the VideoShelf index (DB only — files are not touched).</summary>
    [RelayCommand]
    private void RemoveFromLibrary() => _owner.RemoveOrphanSeries(this);
}

/// <summary>A single empty-creator row (section with zero playable videos).</summary>
public sealed partial class EmptyCreatorRow : ObservableObject
{
    private readonly MissingTriageViewModel _owner;

    public long SectionId { get; }
    public string CreatorName { get; }

    public EmptyCreatorRow(OrphanEntry entry, MissingTriageViewModel owner)
    {
        SectionId = entry.Id;
        CreatorName = entry.CreatorName;
        _owner = owner;
    }

    /// <summary>Removes the creator from the VideoShelf index (DB only — files are not touched).</summary>
    [RelayCommand]
    private void RemoveFromLibrary() => _owner.RemoveEmptyCreator(this);
}

// ── Main VM ───────────────────────────────────────────────────────────────────

/// <summary>
/// M18-F: Missing-file triage sub-VM.
/// Loaded on demand from <see cref="MaintenanceViewModel"/> when the owner navigates to
/// the triage list. Exposed as <see cref="MaintenanceViewModel.Triage"/>.
/// </summary>
public sealed partial class MissingTriageViewModel : ObservableObject
{
    private readonly MaintenanceRepository _maintenance;
    private readonly LibraryRepository _library;
    private readonly IVideoFilePicker _picker;
    private readonly IConfirmService _confirm;

    [ObservableProperty] private string _statusMessage = string.Empty;

    public ObservableCollection<MissingVideoRow> MissingVideos { get; } = new();
    public ObservableCollection<OrphanSeriesRow> OrphanSeries { get; } = new();
    public ObservableCollection<EmptyCreatorRow> EmptyCreators { get; } = new();

    /// <summary>True when any of the three lists is non-empty after Load.</summary>
    public bool HasItems =>
        MissingVideos.Count > 0 || OrphanSeries.Count > 0 || EmptyCreators.Count > 0;

    /// <summary>Raised after a relink or removal so the dashboard tiles can refresh.</summary>
    public event EventHandler? TriageChanged;

    public MissingTriageViewModel(
        MaintenanceRepository maintenance,
        LibraryRepository library,
        IVideoFilePicker picker,
        IConfirmService confirm)
    {
        _maintenance = maintenance;
        _library = library;
        _picker = picker;
        _confirm = confirm;
    }

    /// <summary>Loads (or refreshes) all three lists from the DB.</summary>
    public void Load()
    {
        MissingVideos.Clear();
        foreach (var mv in _maintenance.GetMissingVideos())
            MissingVideos.Add(new MissingVideoRow(mv, this));

        OrphanSeries.Clear();
        foreach (var os in _maintenance.GetOrphanSeries())
            OrphanSeries.Add(new OrphanSeriesRow(os, this));

        EmptyCreators.Clear();
        foreach (var ec in _maintenance.GetEmptyCreators())
            EmptyCreators.Add(new EmptyCreatorRow(ec, this));

        OnPropertyChanged(nameof(HasItems));
        StatusMessage = string.Empty;
    }

    // ── Relink ────────────────────────────────────────────────────────────────

    internal void RelinkManual(MissingVideoRow row)
    {
        // Use the directory of the old (missing) path as the picker hint.
        string? initialFolder = null;
        try { initialFolder = Path.GetDirectoryName(row.FilePath); } catch { }

        var newPath = _picker.PickVideo(initialFolder);
        if (newPath is null) return; // cancelled

        if (!File.Exists(newPath))
        {
            StatusMessage = $"File not found: {newPath}";
            return;
        }

        ApplyRelink(row, newPath);
    }

    internal void AutoFind(MissingVideoRow row)
    {
        // Locate the source root for this video.
        var sourceRoot = _library.GetSourceRootForVideo(row.VideoId);

        if (sourceRoot is null || !Directory.Exists(sourceRoot))
        {
            // Source root not found or doesn't exist — fall back to the manual picker.
            RelinkManual(row);
            return;
        }

        // Load the missing model again to get size/duration (we stored them on load).
        var missingList = _maintenance.GetMissingVideos();
        var missing = missingList.FirstOrDefault(v => v.Id == row.VideoId);
        if (missing is null) return;

        // Walk the source root (not recursing into system dirs) to collect candidates.
        var candidates = new List<CandidateFile>();
        try
        {
            foreach (var filePath in Directory.EnumerateFiles(sourceRoot, "*.*", SearchOption.AllDirectories))
            {
                // Skip the video's own path (it's missing — shouldn't be there, but guard anyway).
                if (string.Equals(filePath, row.FilePath, StringComparison.OrdinalIgnoreCase))
                    continue;
                try
                {
                    var size = new FileInfo(filePath).Length;
                    candidates.Add(new CandidateFile(filePath, size));
                }
                catch { /* skip unreadable entries */ }
            }
        }
        catch { /* directory enumeration failed — fall through */ }

        var match = RelinkMatcher.FindCandidate(missing, candidates);

        if (match is not null)
        {
            // Exactly one candidate — set it on the row so the UI can show a one-click confirm.
            row.AutoFindResult = match;
            StatusMessage = $"Auto-found: {Path.GetFileName(match)}";
            // Immediately apply the relink (plan says "offer one-click relink").
            ApplyRelink(row, match);
        }
        else
        {
            // Ambiguous or no match — fall back to the manual picker.
            row.AutoFindResult = string.Empty; // mark "tried but failed"
            StatusMessage = "Auto-find found no unique match — choose manually.";
            RelinkManual(row);
        }
    }

    private void ApplyRelink(MissingVideoRow row, string newPath)
    {
        _library.RelinkVideo(row.VideoId, row.FilePath, newPath);
        MissingVideos.Remove(row);
        OnPropertyChanged(nameof(HasItems));
        StatusMessage = $"Relinked: {Path.GetFileName(newPath)}";
        TriageChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── Orphan cleanup ────────────────────────────────────────────────────────

    internal void RemoveOrphanSeries(OrphanSeriesRow row)
    {
        if (!_confirm.Confirm(
                "Remove series from library?",
                $"Remove \"{row.Title}\" from VideoShelf?\n\nRemoves from VideoShelf only — your files are not touched."))
            return;

        _maintenance.DeleteSeriesIndex(row.SeriesId);
        OrphanSeries.Remove(row);
        OnPropertyChanged(nameof(HasItems));
        StatusMessage = $"Removed series \"{row.Title}\" from index.";
        TriageChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void RemoveEmptyCreator(EmptyCreatorRow row)
    {
        if (!_confirm.Confirm(
                "Remove creator from library?",
                $"Remove \"{row.CreatorName}\" from VideoShelf?\n\nRemoves from VideoShelf only — your files are not touched."))
            return;

        _maintenance.DeleteSectionIndex(row.SectionId);
        EmptyCreators.Remove(row);
        OnPropertyChanged(nameof(HasItems));
        StatusMessage = $"Removed creator \"{row.CreatorName}\" from index.";
        TriageChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private void Refresh() => Load();
}
