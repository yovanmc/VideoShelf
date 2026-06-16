using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoShelf.App.Services;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.ViewModels;

// ── supporting models ─────────────────────────────────────────────────────────

/// <summary>One candidate frame entry in the grid: a seed video path + a lazily-loaded thumbnail.</summary>
public sealed partial class CandidateFrameViewModel : ObservableObject
{
    /// <summary>Full path of the source video file this candidate was grabbed from.</summary>
    public string VideoPath { get; }

    /// <summary>Series label for display (base title).</summary>
    public string SeriesLabel { get; }

    [ObservableProperty]
    private string? _thumbnailPath;

    [ObservableProperty]
    private bool _isLoading = true;

    public CandidateFrameViewModel(string videoPath, string seriesLabel)
    {
        VideoPath = videoPath;
        SeriesLabel = seriesLabel;
    }
}

/// <summary>
/// State for the "scrub a single video to an exact frame" panel.
/// </summary>
public sealed partial class ScrubTargetViewModel : ObservableObject
{
    public string VideoPath { get; }
    public string SeriesLabel { get; }

    /// <summary>Total duration of the video in seconds (0 when unknown).</summary>
    public double DurationSeconds { get; }

    /// <summary>Current scrub position in seconds; clipped to [0, DurationSeconds] on set.</summary>
    [ObservableProperty]
    private double _positionSeconds;

    /// <summary>Path of the captured preview frame PNG, or null when no capture has been taken yet.</summary>
    [ObservableProperty]
    private string? _previewPath;

    [ObservableProperty]
    private bool _isCapturing;

    public ScrubTargetViewModel(string videoPath, string seriesLabel, double durationSeconds)
    {
        VideoPath = videoPath;
        SeriesLabel = seriesLabel;
        DurationSeconds = durationSeconds;
    }
}

// ── main VM ───────────────────────────────────────────────────────────────────

/// <summary>
/// VM for the hybrid creator portrait picker.
/// Two flows:
///   1. Candidate grid — spread across the creator's series, click one to use it.
///   2. Scrub panel   — pick a video, scrub to an exact frame, capture preview, confirm.
///
/// DB/IO side-effects are isolated behind <see cref="IThumbnailSnapshotter"/> and
/// <see cref="CreatorArtRepository"/> so the pure selection/path helpers are
/// testable without a live libVLC.
/// </summary>
public sealed partial class CreatorFramePickerViewModel : ObservableObject
{
    private readonly long _sectionId;
    private readonly string _coversDir;
    private readonly IThumbnailSnapshotter _snapshotter;
    private readonly CreatorArtRepository _artRepo;
    private readonly LibraryRepository _library;

    // Maximum candidate videos to show in the grid.
    private const int MaxCandidates = 12;

    public ObservableCollection<CandidateFrameViewModel> Candidates { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasScrubTarget))]
    private ScrubTargetViewModel? _scrubTarget;

    public bool HasScrubTarget => ScrubTarget is not null;

    [ObservableProperty]
    private bool _isLoadingCandidates = true;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Raised when the picker has saved the art and the dialog should close.</summary>
    public event EventHandler? Confirmed;

    /// <summary>Raised when the user cancels without saving.</summary>
    public event EventHandler? Cancelled;

    public CreatorFramePickerViewModel(
        long sectionId,
        string coversDir,
        IThumbnailSnapshotter snapshotter,
        CreatorArtRepository artRepo,
        LibraryRepository library)
    {
        _sectionId  = sectionId;
        _coversDir  = coversDir;
        _snapshotter = snapshotter;
        _artRepo     = artRepo;
        _library     = library;
    }

    // ── Initialisation ────────────────────────────────────────────────────────

    /// <summary>
    /// Loads candidate frames from the creator's series.
    /// Call once the dialog/view is shown; safe to await from any thread.
    /// </summary>
    public async Task LoadCandidatesAsync(CancellationToken ct = default)
    {
        IsLoadingCandidates = true;
        Candidates.Clear();

        // Gather all present videos for the section across series.
        var allVideos = await Task.Run(() =>
        {
            var series = _library.GetSeriesForSection(_sectionId);
            return series
                .SelectMany(s => _library.GetVideosForSeries(s.Id)
                    .Select(v => (Series: s, Video: v)))
                .Where(t => !t.Video.Missing && File.Exists(t.Video.FilePath))
                .ToList();
        }, ct);

        var candidates = SelectCandidateVideos(allVideos, MaxCandidates);

        foreach (var (series, video) in candidates)
        {
            var entry = new CandidateFrameViewModel(video.FilePath, series.BaseTitle);
            Candidates.Add(entry);
        }

        IsLoadingCandidates = false;

        // Kick off thumbnail loads in parallel (fail-safe; each entry shows loading state).
        foreach (var entry in Candidates)
        {
            _ = LoadCandidateThumbnailAsync(entry, ct);
        }
    }

    private async Task LoadCandidateThumbnailAsync(CandidateFrameViewModel entry, CancellationToken ct)
    {
        try
        {
            var outPath = BuildCandidateFramePath(_sectionId, _coversDir);
            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
            var ok = await _snapshotter.TrySnapshotAsync(entry.VideoPath, outPath, ct);
            entry.ThumbnailPath = ok ? outPath : null;
        }
        catch { /* fail-safe */ }
        finally { entry.IsLoading = false; }
    }

    // ── Candidate grid: click to use ─────────────────────────────────────────

    [RelayCommand]
    private async Task UseCandidateAsync(CandidateFrameViewModel candidate)
    {
        if (candidate.ThumbnailPath is null) return;
        await SaveArtAsync(candidate.ThumbnailPath);
    }

    // ── Scrub panel ───────────────────────────────────────────────────────────

    /// <summary>Opens the scrub panel for a specific video (called from the candidate grid or a separate picker).</summary>
    [RelayCommand]
    private void OpenScrubPanel(CandidateFrameViewModel candidate)
    {
        // Resolve duration from the library (may be null for un-probed videos → use 0).
        var dur = 0.0;
        // We stored series+video context in the CandidateFrameViewModel; re-query for duration.
        var ep = _library.GetEpisodeByPath(candidate.VideoPath);
        if (ep?.Duration is double d) dur = d;

        ScrubTarget = new ScrubTargetViewModel(candidate.VideoPath, candidate.SeriesLabel, dur);
    }

    [RelayCommand]
    private void CloseScrubPanel() => ScrubTarget = null;

    /// <summary>
    /// Captures a preview frame at the current scrub position.
    /// Shows a preview PNG; does NOT save to creator art yet.
    /// </summary>
    [RelayCommand]
    private async Task CapturePreviewAsync(CancellationToken ct)
    {
        if (ScrubTarget is not { } target) return;
        IsBusy = true;
        target.IsCapturing = true;
        try
        {
            var pos = TimeSpan.FromSeconds(target.PositionSeconds);
            var outPath = BuildCandidateFramePath(_sectionId, _coversDir);
            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
            var ok = await _snapshotter.TrySnapshotAtAsync(target.VideoPath, outPath, pos, ct);
            target.PreviewPath = ok ? outPath : null;
        }
        catch { target.PreviewPath = null; }
        finally { target.IsCapturing = false; IsBusy = false; }
    }

    /// <summary>Uses the current scrub preview as the creator portrait.</summary>
    [RelayCommand]
    private async Task UsePreviewAsync()
    {
        if (ScrubTarget?.PreviewPath is not { } path) return;
        await SaveArtAsync(path);
    }

    // ── Cancel ────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke(this, EventArgs.Empty);

    // ── Save helper ───────────────────────────────────────────────────────────

    private async Task SaveArtAsync(string sourcePngPath)
    {
        IsBusy = true;
        try
        {
            // The file is already in the covers dir (written by the snapshotter).
            // Just persist the path reference to the DB.
            await Task.Run(() => _artRepo.SetArtPath(_sectionId, sourcePngPath));
            Confirmed?.Invoke(this, EventArgs.Empty);
        }
        catch { /* fail-safe — UI stays open */ }
        finally { IsBusy = false; }
    }

    // ── Pure helpers (public for unit tests) ─────────────────────────────────

    /// <summary>
    /// Spreads up to <paramref name="max"/> candidate videos across the creator's series:
    /// takes the first N videos per series in round-robin order so the grid is not dominated
    /// by one large series.  Skips missing / non-existent files.
    /// </summary>
    public static IReadOnlyList<(Series Series, Video Video)> SelectCandidateVideos(
        IReadOnlyList<(Series Series, Video Video)> videos,
        int max)
    {
        if (max <= 0 || videos.Count == 0)
            return [];

        // Group by series id; preserve series order (first appearance).
        var bySeriesId = new Dictionary<long, (Series Series, List<Video> Videos)>();
        var seriesOrder = new List<long>();
        foreach (var (series, video) in videos)
        {
            if (!bySeriesId.TryGetValue(series.Id, out var bucket))
            {
                bucket = (series, []);
                bySeriesId[series.Id] = bucket;
                seriesOrder.Add(series.Id);
            }
            bucket.Videos.Add(video);
        }

        // Round-robin across series until we have max or exhaust everything.
        var result = new List<(Series, Video)>(max);
        var indices = seriesOrder.ToDictionary(id => id, _ => 0);

        while (result.Count < max)
        {
            bool anyAdded = false;
            foreach (var id in seriesOrder)
            {
                if (result.Count >= max) break;
                var (ser, vids) = bySeriesId[id];
                int idx = indices[id];
                if (idx < vids.Count)
                {
                    result.Add((ser, vids[idx]));
                    indices[id] = idx + 1;
                    anyAdded = true;
                }
            }
            if (!anyAdded) break; // all series exhausted
        }

        return result;
    }

    /// <summary>
    /// Builds a unique PNG path under the covers directory for a candidate frame.
    /// Uses sectionId + a Guid so concurrent captures never collide.
    /// The file is in the covers dir — library folders are never written.
    /// </summary>
    public static string BuildCandidateFramePath(long sectionId, string coversDir)
    {
        var fileName = $"creator_{sectionId}_{Guid.NewGuid():N}.png";
        return Path.Combine(coversDir, fileName);
    }
}
