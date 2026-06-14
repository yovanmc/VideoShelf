namespace VideoShelf.App.Services;

public sealed record MediaProbeResult(double? DurationSeconds, int? Width, int? Height);

public interface IMediaProbe
{
    /// <summary>Briefly opens the file in libVLC to read its duration and video resolution.
    /// Returns null duration/dimensions if they can't be determined. Never throws for a bad file.</summary>
    Task<MediaProbeResult> ProbeAsync(string path, CancellationToken cancellationToken);
}
