using System;
using System.Threading;
using System.Threading.Tasks;

namespace VideoShelf.App.Services;

/// <summary>Returns a disk path to a cached poster thumbnail for a video, or null if unavailable.</summary>
public interface IThumbnailService
{
    Task<string?> GetThumbnailPathAsync(string videoPath, CancellationToken cancellationToken);
}

/// <summary>Low-level frame grab. Implementations write a PNG to outputPngPath and return true on success.
/// Must NOT throw — return false on any failure so the cache can fall back to a placeholder.</summary>
public interface IThumbnailSnapshotter
{
    /// <summary>
    /// Grabs a representative frame (default position) and writes it as a PNG to
    /// <paramref name="outputPngPath"/>. Returns true on success, false on any failure.
    /// Must NOT throw.
    /// </summary>
    Task<bool> TrySnapshotAsync(string videoPath, string outputPngPath, CancellationToken cancellationToken);

    /// <summary>
    /// Seeks to <paramref name="position"/> (clamped to [0, duration]) then grabs a frame.
    /// Returns true on success, false on any failure. Must NOT throw.
    /// </summary>
    Task<bool> TrySnapshotAtAsync(string videoPath, string outputPngPath, TimeSpan position, CancellationToken ct);
}

/// <summary>
/// Pure helpers for computing snapshot seek positions.
/// Stateless and fully unit-testable without libVLC.
/// </summary>
public static class SnapshotPositionHelper
{
    /// <summary>
    /// Default seek fraction used when no explicit position is requested (10% of duration, capped at 3 s).
    /// </summary>
    public static readonly double DefaultFractionOfDuration = 0.10;

    /// <summary>
    /// Clamps <paramref name="requested"/> to [0, <paramref name="duration"/>].
    /// When <paramref name="duration"/> is null or zero, returns <see cref="TimeSpan.Zero"/>.
    /// </summary>
    public static TimeSpan Clamp(TimeSpan requested, TimeSpan? duration)
    {
        if (duration is null || duration.Value <= TimeSpan.Zero)
            return TimeSpan.Zero;
        if (requested < TimeSpan.Zero)
            return TimeSpan.Zero;
        if (requested > duration.Value)
            return duration.Value;
        return requested;
    }

    /// <summary>
    /// Returns the default snapshot position: 10 % of <paramref name="durationMs"/> (in ms),
    /// capped at 3000 ms. Returns 0 when duration is unknown (≤ 0).
    /// </summary>
    public static long DefaultSeekMs(long durationMs)
    {
        if (durationMs <= 0) return 0;
        return Math.Min(durationMs / 10, 3000);
    }
}
