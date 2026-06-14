using System.Collections.Generic;

namespace VideoShelf.Core.Models;

// ── M18-D: Maintenance dashboard models ──────────────────────────────────────

public sealed record DuplicateVideo(
    long Id,
    long SectionId,
    string CreatorName,
    string SeriesTitle,
    string FilePath,
    long? SizeBytes,
    double? DurationSeconds,
    int? Width,
    int? Height);

public sealed record DuplicateGroup(
    long SizeBytes,
    int DurationRoundedSeconds,
    IReadOnlyList<DuplicateVideo> Videos);

public sealed record MaintenanceSummary(
    int MissingCount,
    int OrphanSeriesCount,
    int EmptyCreatorCount,
    int SingleFileSeriesCount,
    int DuplicateGroupCount,
    long DbSizeBytes);

public sealed record MissingVideo(
    long Id,
    string FilePath,
    string CreatorName,
    string SeriesTitle);

public sealed record OrphanEntry(
    long Id,
    string Title,
    string CreatorName);
