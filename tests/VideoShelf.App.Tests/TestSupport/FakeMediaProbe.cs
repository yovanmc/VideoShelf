using System.Threading;
using System.Threading.Tasks;
using VideoShelf.App.Services;

namespace VideoShelf.App.Tests.TestSupport;

/// <summary>A configurable IMediaProbe for unit tests.
/// Returns the result set on <see cref="Result"/> for every file probed.
/// Optionally records peak concurrency when <see cref="SimulateDelayMs"/> is set.</summary>
public sealed class FakeMediaProbe : IMediaProbe
{
    /// <summary>The result to return for every ProbeAsync call. Defaults to (null, null, null).</summary>
    public MediaProbeResult Result { get; set; } = new MediaProbeResult(null, null, null);

    /// <summary>When set, each probe waits this many milliseconds — enabling concurrency measurement.</summary>
    public int SimulateDelayMs { get; set; } = 0;

    private int _current;
    private int _maxObserved;

    /// <summary>Peak number of concurrent ProbeAsync calls observed. Only meaningful when
    /// <see cref="SimulateDelayMs"/> is set so calls overlap in time.</summary>
    public int MaxObservedConcurrency => _maxObserved;

    public FakeMediaProbe() { }

    /// <summary>Convenience constructor for concurrency tests.</summary>
    public FakeMediaProbe(double durationSeconds, int width, int height)
    {
        Result = new MediaProbeResult(durationSeconds, width, height);
        SimulateDelayMs = 10; // small delay so concurrent calls actually overlap
    }

    public async Task<MediaProbeResult> ProbeAsync(string path, CancellationToken cancellationToken)
    {
        int c = Interlocked.Increment(ref _current);
        // Update peak (lock-free CAS loop)
        int observed;
        do { observed = _maxObserved; }
        while (c > observed && Interlocked.CompareExchange(ref _maxObserved, c, observed) != observed);

        if (SimulateDelayMs > 0)
            await Task.Delay(SimulateDelayMs, cancellationToken);

        Interlocked.Decrement(ref _current);
        return Result;
    }
}
