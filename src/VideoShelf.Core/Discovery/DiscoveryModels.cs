namespace VideoShelf.Core.Discovery;

/// <summary>A resumable video for the Continue-watching rail.</summary>
public sealed record ContinueWatchingItem(
    long VideoId, long SeriesId, long SectionId, string SeriesTitle, bool IsStandalone,
    int EpisodeNo, double ResumePosition, double? Duration, string? ThumbnailSeedPath);

/// <summary>A video for the Recently-added / Recently-watched rails.</summary>
public sealed record RecencyItem(
    long VideoId, long SeriesId, long SectionId, string SeriesTitle, bool IsStandalone,
    int EpisodeNo, bool Watched, string? ThumbnailSeedPath);

/// <summary>A scored section for For-you / Pick-a-tag / More-from-section rails.</summary>
public sealed record SectionSuggestion(
    long SectionId, string DisplayName, int SeriesCount, int EpisodeCount, int UnwatchedCount,
    IReadOnlyList<string> Tags, double Score);

/// <summary>One (tag, time) pair derived from the watch history, for affinity scoring.</summary>
public sealed record WatchedTag(string Tag, DateTimeOffset WatchedAt);
