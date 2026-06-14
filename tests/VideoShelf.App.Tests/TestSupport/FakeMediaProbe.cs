using System.Threading;
using System.Threading.Tasks;
using VideoShelf.App.Services;

namespace VideoShelf.App.Tests.TestSupport;

/// <summary>A configurable IMediaProbe for unit tests.
/// Returns the result set on <see cref="Result"/> for every file probed.</summary>
public sealed class FakeMediaProbe : IMediaProbe
{
    /// <summary>The result to return for every ProbeAsync call. Defaults to (null, null, null).</summary>
    public MediaProbeResult Result { get; set; } = new MediaProbeResult(null, null, null);

    public Task<MediaProbeResult> ProbeAsync(string path, CancellationToken cancellationToken)
        => Task.FromResult(Result);
}
