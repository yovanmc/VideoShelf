using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoShelf.App.Services;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.ViewModels;

/// <summary>Per-source row shown on the maintenance dashboard.</summary>
public sealed partial class SourceHealthRow : ObservableObject
{
    public long SourceId { get; }
    public string DisplayName { get; }
    public string RootPath { get; }
    public int VideoCount { get; }
    public string LastScanText { get; }

    private readonly IScanCoordinator _coordinator;

    [ObservableProperty]
    private bool _isRescanning;

    public SourceHealthRow(
        long sourceId,
        string displayName,
        string rootPath,
        int videoCount,
        DateTimeOffset? lastScanUtc,
        IScanCoordinator coordinator)
    {
        SourceId = sourceId;
        DisplayName = displayName;
        RootPath = rootPath;
        VideoCount = videoCount;
        LastScanText = FormatLastScan(lastScanUtc);
        _coordinator = coordinator;
    }

    private static string FormatLastScan(DateTimeOffset? utc)
    {
        if (utc is null) return "Never";
        var ago = DateTimeOffset.UtcNow - utc.Value;
        if (ago.TotalMinutes < 1)  return "Just now";
        if (ago.TotalMinutes < 60) return $"{(int)ago.TotalMinutes} min ago";
        if (ago.TotalHours < 24)   return $"{(int)ago.TotalHours} h ago";
        return $"{(int)ago.TotalDays} d ago";
    }

    [RelayCommand]
    private async Task RescanSourceAsync()
    {
        if (_coordinator.IsBusy) return;
        IsRescanning = true;
        try
        {
            await _coordinator.ScanAllAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            IsRescanning = false;
        }
    }
}

/// <summary>
/// Dashboard VM for the M18 Maintenance page.
/// Loads <see cref="MaintenanceSummary"/>, per-source health rows, and exposes the
/// last scan-diff banner text.  No live WPF dependency — fully unit-testable.
/// </summary>
public sealed partial class MaintenanceViewModel : ObservableObject
{
    private readonly MaintenanceRepository _maintenance;
    private readonly LibraryRepository _library;
    private readonly IScanCoordinator _coordinator;

    // ── Summary tile properties ───────────────────────────────────────────────

    [ObservableProperty] private int _missingCount;
    [ObservableProperty] private int _orphanSeriesCount;
    [ObservableProperty] private int _emptyCreatorCount;
    [ObservableProperty] private int _singleFileSeriesCount;
    [ObservableProperty] private int _duplicateGroupCount;
    [ObservableProperty] private string _dbSizeText = "–";
    [ObservableProperty] private string _scanSummaryText = string.Empty;

    /// <summary>Per-source health cards.</summary>
    public IReadOnlyList<SourceHealthRow> SourceRows { get; private set; } = Array.Empty<SourceHealthRow>();

    public MaintenanceViewModel(
        MaintenanceRepository maintenance,
        LibraryRepository library,
        IScanCoordinator coordinator)
    {
        _maintenance = maintenance;
        _library = library;
        _coordinator = coordinator;
    }

    /// <summary>Loads (or refreshes) all maintenance data. Call on navigation.</summary>
    public void Load()
    {
        var summary = _maintenance.GetMaintenanceSummary();

        MissingCount          = summary.MissingCount;
        OrphanSeriesCount     = summary.OrphanSeriesCount;
        EmptyCreatorCount     = summary.EmptyCreatorCount;
        SingleFileSeriesCount = summary.SingleFileSeriesCount;
        DuplicateGroupCount   = summary.DuplicateGroupCount;
        DbSizeText            = FormatBytes(summary.DbSizeBytes);

        // Per-source rows: build from library sources + their last-scan times.
        var sources = _library.GetSources();
        SourceRows = sources
            .Select(s => new SourceHealthRow(
                s.Id,
                s.DisplayName,
                s.RootPath,
                CountVideosForSource(s.Id),
                _library.GetSourceLastScanUtc(s.Id),
                _coordinator))
            .ToList();

        OnPropertyChanged(nameof(SourceRows));
    }

    /// <summary>
    /// Sets the scan-diff banner text ("Added 12 · updated 3 · restored 1 · missing 1").
    /// Called by Group I (scan-diff surfacing) after each scan.
    /// </summary>
    public void SetScanSummary(string text)
        => ScanSummaryText = text;

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private void Refresh() => Load();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private int CountVideosForSource(long sourceId)
    {
        // Use the sections count as a proxy (simple and fast).
        // Returns the number of non-missing videos under this source.
        var sections = _library.GetSections(sourceId);
        return sections.Sum(sec =>
        {
            var series = _library.GetSeriesForSection(sec.Id);
            return series.Sum(se => _library.GetEpisodes(se.Id).Count);
        });
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        if (bytes < 1024L * 1024)           return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024)    return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }
}
