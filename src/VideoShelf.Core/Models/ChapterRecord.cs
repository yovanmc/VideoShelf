namespace VideoShelf.Core.Models;

/// <summary>A persisted chapter marker for a video (probed from libVLC at scan time).</summary>
public sealed record ChapterRecord(int Index, string Name, double StartSeconds);

/// <summary>A video that still needs a libVLC duration/chapter probe.</summary>
public sealed record VideoToProbe(long Id, string FilePath);
