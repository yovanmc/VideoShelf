using System;
using System.Threading.Tasks;

namespace VideoShelf.App.Tests.TestSupport;

/// <summary>
/// Deterministic poll-until helpers for tests that invoke fire-and-forget
/// async wrappers (e.g. <c>void Load() => _ = LoadAsync()</c>) and need to
/// wait for the async continuation to complete without relying on a fixed delay.
/// </summary>
public static class TestWait
{
    /// <summary>
    /// Polls <paramref name="condition"/> every <paramref name="pollIntervalMs"/> milliseconds
    /// until it returns <c>true</c> or <paramref name="timeoutMs"/> elapses.
    /// Returns <c>true</c> if the condition was met, <c>false</c> on timeout.
    /// </summary>
    public static async Task<bool> UntilAsync(
        Func<bool> condition,
        int timeoutMs = 5000,
        int pollIntervalMs = 15)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            await Task.Delay(pollIntervalMs);
        }
        return condition(); // one final check at/after deadline
    }
}
