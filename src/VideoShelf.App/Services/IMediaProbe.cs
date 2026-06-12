using VideoShelf.Core.Models;

namespace VideoShelf.App.Services;

public sealed record MediaProbeResult(double? DurationSeconds, IReadOnlyList<ChapterRecord> Chapters);

public interface IMediaProbe
{
    /// <summary>Briefly opens the file in libVLC to read its duration and chapter markers.
    /// Returns null duration if it can't be determined. Never throws for a bad file — returns (null, empty).</summary>
    Task<MediaProbeResult> ProbeAsync(string path, CancellationToken cancellationToken);
}
