using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoShelf.App.Services;
using VideoShelf.Core.Models;
using VideoShelf.Core.Renaming;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.ViewModels;

// ── Candidate row ─────────────────────────────────────────────────────────────

/// <summary>One candidate video within a <see cref="DuplicateResolveViewModel"/>.</summary>
public sealed partial class DuplicateVideoRow : ObservableObject
{
    private readonly DuplicateResolveViewModel _owner;

    public long VideoId { get; }
    public string FilePath { get; }
    public string FileName => Path.GetFileName(FilePath);
    public string CreatorName { get; }
    public string SeriesTitle { get; }

    /// <summary>Human-readable file size, e.g. "1.23 GB" or "–" if unknown.</summary>
    public string SizeText { get; }

    /// <summary>Human-readable duration, e.g. "1:23:45" or "–" if unknown.</summary>
    public string DurationText { get; }

    /// <summary>Resolution string, e.g. "1920×1080" or "–" if unknown.</summary>
    public string ResolutionText { get; }

    public DuplicateVideoRow(DuplicateVideo model, DuplicateResolveViewModel owner)
    {
        _owner = owner;
        VideoId    = model.Id;
        FilePath   = model.FilePath;
        CreatorName  = model.CreatorName;
        SeriesTitle  = model.SeriesTitle;
        SizeText     = FormatSize(model.SizeBytes);
        DurationText = FormatDuration(model.DurationSeconds);
        ResolutionText = (model.Width.HasValue && model.Height.HasValue)
            ? $"{model.Width.Value}×{model.Height.Value}"
            : "–";
    }

    /// <summary>Routes to the existing player flow via the owner.</summary>
    [RelayCommand]
    private void Play() => _owner.RequestPlay(this);

    /// <summary>Keeps this video and recycles all others in the group.</summary>
    [RelayCommand]
    private void Keep() => _owner.ResolveWithKeeper(this);

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string FormatSize(long? bytes)
    {
        if (bytes is null || bytes < 0) return "–";
        if (bytes < 1024L * 1024)       return $"{bytes.Value / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes.Value / (1024.0 * 1024):F1} MB";
        return $"{bytes.Value / (1024.0 * 1024 * 1024):F2} GB";
    }

    private static string FormatDuration(double? seconds)
    {
        if (seconds is null) return "–";
        var ts = TimeSpan.FromSeconds(seconds.Value);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes}:{ts.Seconds:D2}";
    }
}

// ── Main VM ───────────────────────────────────────────────────────────────────

/// <summary>
/// M18-G: duplicate compare and resolve.
/// Shown for a single <see cref="DuplicateGroup"/>; owner picks the keeper, all others
/// are sent to the Recycle Bin and removed from the DB index.
/// The <see cref="Resolved"/> event fires on success or dismiss so the dashboard / creator
/// page can refresh their duplicate counts.
/// </summary>
public sealed partial class DuplicateResolveViewModel : ObservableObject
{
    private readonly MaintenanceRepository _maintenance;
    private readonly LibraryRepository _library;
    private readonly IRecycleBinService _recycleBin;
    private readonly IConfirmService _confirm;
    private readonly IFileSystem _fs;
    private readonly DuplicateGroup _group;

    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isError;

    /// <summary>Raised after a successful Keep resolution or a NotADuplicate dismissal.</summary>
    public event EventHandler? Resolved;

    /// <summary>
    /// Raised when the owner wants to play a candidate.
    /// The <c>string</c> arg is the file path to play.
    /// </summary>
    public event EventHandler<string>? PlayRequested;

    public ObservableCollection<DuplicateVideoRow> Candidates { get; } = new();

    public DuplicateResolveViewModel(
        DuplicateGroup group,
        MaintenanceRepository maintenance,
        LibraryRepository library,
        IRecycleBinService recycleBin,
        IConfirmService confirm,
        IFileSystem fs)
    {
        _group = group;
        _maintenance = maintenance;
        _library = library;
        _recycleBin = recycleBin;
        _confirm = confirm;
        _fs = fs;

        foreach (var v in group.Videos)
            Candidates.Add(new DuplicateVideoRow(v, this));
    }

    // ── Play routing ─────────────────────────────────────────────────────────

    internal void RequestPlay(DuplicateVideoRow row)
        => PlayRequested?.Invoke(this, row.FilePath);

    // ── Keeper resolution ─────────────────────────────────────────────────────

    /// <summary>
    /// Keeps <paramref name="keeper"/> and recycles all other candidates.
    ///
    /// SAFETY GATE: before recycling any non-keeper, verify the keeper file exists on
    /// disk AND has a non-zero byte length. If the keeper is missing or zero-length,
    /// ABORT immediately — nothing is recycled, nothing is DB-deleted, and an error
    /// message is surfaced.
    /// </summary>
    internal void ResolveWithKeeper(DuplicateVideoRow keeper)
    {
        // ── Safety gate: verify keeper present + non-zero ─────────────────────
        var keeperLength = _fs.GetFileLength(keeper.FilePath);
        if (!CanRecycleLosers(keeperLength))
        {
            var reason = keeperLength == -1
                ? $"Keeper file not found on disk:\n{keeper.FilePath}"
                : $"Keeper file is zero bytes:\n{keeper.FilePath}";
            SetError($"Cannot resolve — {reason}\nNo files were deleted.");
            return;
        }

        // ── Confirm ───────────────────────────────────────────────────────────
        var losers = _group.Videos.Where(v => v.Id != keeper.VideoId).ToList();
        var loserNames = string.Join("\n  • ", losers.Select(v => Path.GetFileName(v.FilePath)));
        if (!_confirm.Confirm(
                "Send to Recycle Bin?",
                $"Keep: {Path.GetFileName(keeper.FilePath)}\n\nSend to Recycle Bin:\n  • {loserNames}"))
        {
            return;  // user declined
        }

        // ── Recycle + DB-delete each non-keeper ───────────────────────────────
        var errors = new System.Text.StringBuilder();
        foreach (var loser in losers)
        {
            if (!_recycleBin.SendToRecycleBin(loser.FilePath))
            {
                errors.AppendLine($"Failed to recycle: {Path.GetFileName(loser.FilePath)}");
                continue;
            }
            _library.DeleteVideoIndexById(loser.Id);
        }

        if (errors.Length > 0)
        {
            SetError($"Partial resolve — some files could not be recycled:\n{errors}");
            // Still raise Resolved so the list refreshes (the kept+successfully-recycled
            // entries are gone from the duplicate list).
        }
        else
        {
            StatusMessage = $"Resolved — kept {Path.GetFileName(keeper.FilePath)}.";
            IsError = false;
        }

        Resolved?.Invoke(this, EventArgs.Empty);
    }

    // ── Dismiss ("not a duplicate") ───────────────────────────────────────────

    /// <summary>
    /// Dismisses all cross-pairs in this group so they never re-flag as duplicates.
    /// </summary>
    [RelayCommand]
    private void NotADuplicate()
    {
        var now = DateTimeOffset.UtcNow;
        var videos = _group.Videos.ToList();
        for (var i = 0; i < videos.Count; i++)
            for (var j = i + 1; j < videos.Count; j++)
                _maintenance.DismissDuplicatePair(videos[i].Id, videos[j].Id, now);

        StatusMessage = "Marked as not a duplicate.";
        IsError = false;
        Resolved?.Invoke(this, EventArgs.Empty);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Pure safety predicate for the keeper-gate: the losers may only be recycled when the
    /// keeper file is present on disk AND has a non-zero byte length.
    /// <paramref name="keeperLength"/> is the value returned by <see cref="IFileSystem.GetFileLength"/>:
    /// <c>-1</c> when the file is missing, <c>0</c> when it is empty, otherwise the byte length.
    /// Returns <c>true</c> only when <paramref name="keeperLength"/> is strictly positive.
    /// Extracted so the destroy-only-when-keeper-is-safe decision is unit-testable in isolation.
    /// </summary>
    public static bool CanRecycleLosers(long keeperLength) => keeperLength > 0;

    private void SetError(string message)
    {
        StatusMessage = message;
        IsError = true;
    }
}
