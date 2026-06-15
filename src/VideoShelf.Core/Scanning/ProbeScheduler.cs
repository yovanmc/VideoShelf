namespace VideoShelf.Core.Scanning;

public static class ProbeScheduler
{
    /// <summary>Runs <paramref name="work"/> over items with at most <paramref name="degree"/> in flight.
    /// degree=1 is exactly sequential. Cancellation propagates as OperationCanceledException.</summary>
    public static async Task RunAsync<T>(
        IReadOnlyList<T> items, int degree, Func<T, CancellationToken, Task> work, CancellationToken ct)
    {
        degree = Math.Max(1, degree);
        using var sem = new SemaphoreSlim(degree);
        var tasks = new List<Task>(items.Count);
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            await sem.WaitAsync(ct);
            tasks.Add(Task.Run(async () =>
            {
                try { await work(item, ct); }
                finally { sem.Release(); }
            }, ct));
        }
        await Task.WhenAll(tasks);
    }
}
