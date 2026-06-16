using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.ViewModels;

// ── Bar-chart item (used for ratings, activity, and tags charts) ───────────

/// <summary>One bar in a border-based bar chart. Fraction is 0..1; Label and ValueText are display strings.</summary>
public sealed record InsightsBarItem(string Label, string ValueText, double Fraction);

// ── InsightsViewModel ───────────────────────────────────────────────────────

/// <summary>
/// ViewModel for the dedicated Insights dashboard page (M24-E).
/// Loads no-schema aggregates via StatsRepository and exposes them as stat
/// strings and bar-chart collections (InsightsBarItem, Fraction 0..1).
/// </summary>
public sealed partial class InsightsViewModel : ObservableObject
{
    private readonly StatsRepository _stats;

    public InsightsViewModel(StatsRepository stats)
    {
        _stats = stats;
    }

    // ── Stat cards ─────────────────────────────────────────────────────────

    [ObservableProperty] private string _totalVideosText  = "–";
    [ObservableProperty] private string _watchedText      = "–";
    [ObservableProperty] private string _completionText   = "–";
    [ObservableProperty] private string _totalHoursText   = "–";
    [ObservableProperty] private string _creatorsText     = "–";
    [ObservableProperty] private string _seriesText       = "–";
    [ObservableProperty] private string _standalonesText  = "–";

    // ── Empty-library guard ────────────────────────────────────────────────

    [ObservableProperty] private bool _hasData;

    // ── Bar-chart collections ──────────────────────────────────────────────

    public ObservableCollection<InsightsBarItem> WatchActivityBars { get; } = [];
    public ObservableCollection<InsightsBarItem> RatingBars        { get; } = [];
    public ObservableCollection<InsightsBarItem> TopCreatorBars    { get; } = [];
    public ObservableCollection<InsightsBarItem> TopTagBars        { get; } = [];

    // ── Section visibility ─────────────────────────────────────────────────

    [ObservableProperty] private bool _hasWatchActivity;
    [ObservableProperty] private bool _hasRatings;
    [ObservableProperty] private bool _hasTopCreators;
    [ObservableProperty] private bool _hasTopTags;

    // ── Load ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads all insights data synchronously (queries are fast aggregates).
    /// Called from MainViewModel.ShowInsights before navigation, mirroring the
    /// Maintenance.Load() / History.Load() pattern.
    /// </summary>
    public void Load()
    {
        // ── Library stats (stat cards) ─────────────────────────────────────
        var lib = _stats.GetLibraryStats();
        TotalVideosText = lib.TotalVideos.ToString();
        WatchedText     = lib.WatchedVideos.ToString();

        if (lib.TotalVideos > 0)
        {
            var pct = (int)Math.Round(lib.WatchedVideos * 100.0 / lib.TotalVideos);
            CompletionText = $"{pct}%";
        }
        else
        {
            CompletionText = "0%";
        }

        var totalHours = lib.WatchedDurationSeconds / 3600.0;
        TotalHoursText = totalHours >= 1
            ? $"{totalHours:F1} h"
            : $"{(int)(lib.WatchedDurationSeconds / 60)} min";

        // ── Library composition ────────────────────────────────────────────
        var comp = _stats.GetLibraryComposition();
        CreatorsText    = comp.Creators.ToString();
        SeriesText      = comp.Series.ToString();
        StandalonesText = comp.Standalones.ToString();

        HasData = comp.TotalVideos > 0 || comp.Creators > 0;

        // ── Watch activity by month (last 12 months) ───────────────────────
        WatchActivityBars.Clear();
        var activity = _stats.GetWatchActivityByMonth(12);
        if (activity.Count > 0)
        {
            var maxAct = activity.Max(p => p.Count);
            foreach (var pt in activity)
            {
                var fraction = maxAct > 0 ? (double)pt.Count / maxAct : 0;
                // Period is "YYYY-MM" → shorten to "MMM" for the label
                var label = FormatPeriodLabel(pt.Period);
                WatchActivityBars.Add(new InsightsBarItem(label, pt.Count.ToString(), fraction));
            }
        }
        HasWatchActivity = WatchActivityBars.Count > 0;

        // ── Rating distribution ────────────────────────────────────────────
        RatingBars.Clear();
        var ratings = _stats.GetRatingDistribution();
        // Omit the 0-bucket if every video is unrated (all fall in 0) — only show
        // it when there are also rated videos, to avoid a "100% unrated" chart.
        var ratedBuckets = ratings.Where(b => b.Rating > 0).ToList();
        if (ratedBuckets.Count > 0)
        {
            var maxRat = ratings.Max(b => b.Count);
            foreach (var b in ratings)
            {
                var fraction = maxRat > 0 ? (double)b.Count / maxRat : 0;
                var label    = b.Rating == 0.0 ? "Unrated" : $"{b.Rating:G}★";
                RatingBars.Add(new InsightsBarItem(label, b.Count.ToString(), fraction));
            }
        }
        HasRatings = RatingBars.Count > 0;

        // ── Top creators by watched ────────────────────────────────────────
        TopCreatorBars.Clear();
        var creators = _stats.GetTopCreatorsByWatched(8);
        if (creators.Count > 0)
        {
            var maxC = creators.Max(c => c.WatchedCount);
            foreach (var c in creators)
            {
                var fraction = maxC > 0 ? (double)c.WatchedCount / maxC : 0;
                TopCreatorBars.Add(new InsightsBarItem(c.Name, c.WatchedCount.ToString(), fraction));
            }
        }
        HasTopCreators = TopCreatorBars.Count > 0;

        // ── Top tags by total video count ──────────────────────────────────
        TopTagBars.Clear();
        var tagStats = _stats.GetTopTagsByWatch(8);
        if (tagStats.Count > 0)
        {
            var maxT = tagStats.Max(t => t.Total);
            foreach (var t in tagStats)
            {
                var fraction = maxT > 0 ? (double)t.Total / maxT : 0;
                TopTagBars.Add(new InsightsBarItem(t.Tag, $"{t.Watched}/{t.Total}", fraction));
            }
        }
        HasTopTags = TopTagBars.Count > 0;
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    /// <summary>Converts "YYYY-MM" to a short month label like "Jan 25".</summary>
    private static string FormatPeriodLabel(string period)
    {
        if (period.Length == 7
            && int.TryParse(period.AsSpan(0, 4), out var yr)
            && int.TryParse(period.AsSpan(5, 2), out var mo)
            && mo >= 1 && mo <= 12)
        {
            var dt = new DateTime(yr, mo, 1);
            return dt.ToString("MMM yy");
        }
        return period;
    }
}
