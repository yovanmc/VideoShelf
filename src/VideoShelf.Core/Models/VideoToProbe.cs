namespace VideoShelf.Core.Models;

/// <summary>A video that still needs a libVLC duration/resolution probe.</summary>
public sealed record VideoToProbe(long Id, string FilePath);
