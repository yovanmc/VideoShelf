using VideoShelf.Core.Scanning;

namespace VideoShelf.Core.Tests.Scanning;

public class ProbeSchedulerTests
{
    [Fact]
    public async Task Runs_all_items_with_bounded_concurrency()
    {
        int current = 0, peak = 0;
        var gate = new object();
        var items = Enumerable.Range(0, 50).ToList();

        await ProbeScheduler.RunAsync(items, degree: 4, async (i, ct) =>
        {
            lock (gate) { current++; peak = Math.Max(peak, current); }
            await Task.Delay(5, ct);
            lock (gate) { current--; }
        }, CancellationToken.None);

        Assert.True(peak <= 4, $"peak concurrency {peak} exceeded degree 4");
    }

    [Fact]
    public async Task Honors_cancellation()
    {
        using var cts = new CancellationTokenSource();
        var started = 0;
        var task = ProbeScheduler.RunAsync(Enumerable.Range(0, 1000).ToList(), degree: 2, async (i, ct) =>
        {
            Interlocked.Increment(ref started);
            if (started == 3) cts.Cancel();
            await Task.Delay(10, ct);
        }, cts.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.True(started < 1000);
    }
}
