namespace VideoShelf.Core.Models;
public sealed record Video(
    long Id, long SeriesId, string FilePath, int EpisodeNo, string RawFilename,
    string Format, double? Duration, string? ThumbnailPath, bool Watched);
