namespace VideoShelf.Core.Models;

public sealed record LibraryStats(int TotalVideos, int WatchedVideos, int InProgressVideos, double WatchedDurationSeconds);
public sealed record CreatorWatchCount(long SectionId, string Name, int WatchedCount);

// ── E1: Insights stats models ────────────────────────────────────────────────

/// <summary>Rating bucket for the ratings-distribution bar chart (0..5 in 0.5 steps).</summary>
public sealed record RatingBucket(double Rating, int Count);

/// <summary>One month's watch-event count for the activity-over-time bar chart.</summary>
public sealed record WatchActivityPoint(string Period, int Count);

/// <summary>Per-tag counts: how many videos have the tag and how many are watched.</summary>
public sealed record TagWatchStat(string Tag, int Total, int Watched);

/// <summary>High-level library composition numbers for the stat cards.</summary>
public sealed record LibraryComposition(int Creators, int Series, int Standalones, int TotalVideos, double TotalDurationSeconds);
