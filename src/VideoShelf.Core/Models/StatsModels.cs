namespace VideoShelf.Core.Models;

public sealed record LibraryStats(int TotalVideos, int WatchedVideos, int InProgressVideos, double WatchedDurationSeconds);
public sealed record CreatorWatchCount(long SectionId, string Name, int WatchedCount);
