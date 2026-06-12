namespace VideoShelf.Core.Models;

/// <summary>A section as shown in the browse sidebar, with its aggregated unwatched count.</summary>
public sealed record SectionSummary(
    long SectionId, long SourceId, string DisplayName, int SeriesCount, int UnwatchedCount,
    int VideoCount, string? ThumbnailSeedPath);

/// <summary>A series or standalone card: episode/unwatched counts plus a thumbnail seed (first episode path).</summary>
public sealed record SeriesSummary(
    long SeriesId, long SectionId, string BaseTitle, bool IsStandalone,
    int EpisodeCount, int UnwatchedCount, string? ThumbnailSeedPath);

/// <summary>An episode row: identity, ordering, display title, and watched/missing flags.</summary>
public sealed record EpisodeView(
    long VideoId, long SeriesId, string FilePath, int EpisodeNo, string Title,
    bool Watched, bool Missing);

public enum SearchHitKind { Section, Series, Video }

/// <summary>One search result. TargetId is the section/series/video id matching Kind; SectionId
/// is the owning section (for jump-to-library navigation). For sections, SectionId == TargetId.</summary>
public sealed record SearchHit(SearchHitKind Kind, long TargetId, long SectionId, string Title);

public enum BrowseSort { Name, DateAdded, RecentlyWatched }
