using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Threading;
using System.Threading.Tasks;
using Shouldly;
using VideoShelf.App.Services;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Scanning;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;

namespace VideoShelf.App.Tests;

/// <summary>
/// Verifies that ObservableCollection mutations in the ViewModels happen on the
/// SynchronizationContext's thread, not a thread-pool thread.  If ConfigureAwait(false)
/// is reintroduced on a bound-collection-mutation chain, the CollectionChanged handler
/// will fire on a thread-pool thread and the assertion will fail.
/// </summary>
public class UiThreadAffinityTests
{
    private sealed class NullThumbs : IThumbnailService
    {
        public Task<string?> GetThumbnailPathAsync(string videoPath, CancellationToken ct)
            => Task.FromResult<string?>(null);
    }

    /// <summary>
    /// A SynchronizationContext that pumps all callbacks on a single dedicated thread.
    /// Continuations posted to it will execute on <see cref="ThreadId"/>.
    /// </summary>
    private sealed class SingleThreadPumpContext : SynchronizationContext, IDisposable
    {
        private readonly Thread _thread;
        private readonly System.Collections.Concurrent.BlockingCollection<(SendOrPostCallback, object?)> _queue
            = new();

        public int ThreadId => _thread.ManagedThreadId;

        public SingleThreadPumpContext()
        {
            _thread = new Thread(Pump) { IsBackground = true, Name = "UiTestPump" };
            _thread.Start();
        }

        private void Pump()
        {
            SetSynchronizationContext(this);
            foreach (var (cb, state) in _queue.GetConsumingEnumerable())
                cb(state);
        }

        public override void Post(SendOrPostCallback d, object? state)
            => _queue.Add((d, state));

        public override void Send(SendOrPostCallback d, object? state)
        {
            if (Thread.CurrentThread == _thread)
            {
                d(state);
                return;
            }
            using var done = new ManualResetEventSlim(false);
            _queue.Add((s =>
            {
                d(s);
                done.Set();
            }, state));
            done.Wait();
        }

        /// <summary>Runs an async delegate on the pump thread and waits for completion.</summary>
        public void RunOnPumpThread(Func<Task> asyncAction)
        {
            Exception? ex = null;
            var done = new ManualResetEventSlim(false);
            Post(_ =>
            {
                // Start the async work; when it completes signal the waiter.
                asyncAction()
                    .ContinueWith(t =>
                    {
                        if (t.IsFaulted) ex = t.Exception!.InnerException ?? t.Exception;
                        done.Set();
                    });
            }, null);
            done.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue("async action did not complete within 10 s");
            if (ex is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex).Throw();
        }

        public void Dispose() => _queue.CompleteAdding();
    }

    [Fact]
    public void LoadSectionsAsync_CollectionChanged_fires_on_context_thread()
    {
        using var temp = new AppTempDb();
        using var dir = new TempDir();
        dir.Touch("Creator A/Cool Story.mp4");
        var lib = new LibraryRepository(temp.Db);
        var watch = new WatchRepository(temp.Db);
        new ScanService(temp.Db, lib).ScanSource(dir.Path, "V");
        var vm = new LibraryViewModel(lib, watch, new NullThumbs());

        using var ctx = new SingleThreadPumpContext();
        var contextThreadId = ctx.ThreadId;
        var mutationThreadIds = new List<int>();

        vm.Sections.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Add ||
                e.Action == NotifyCollectionChangedAction.Reset)
                mutationThreadIds.Add(Thread.CurrentThread.ManagedThreadId);
        };

        ctx.RunOnPumpThread(() => vm.LoadSectionsAsync());

        // At least one mutation (Add or Reset) must have been observed.
        mutationThreadIds.ShouldNotBeEmpty();

        // Every mutation must have occurred on the pump/context thread, not a pool thread.
        foreach (var tid in mutationThreadIds)
            tid.ShouldBe(contextThreadId,
                $"CollectionChanged fired on thread {tid} but expected context thread {contextThreadId}; " +
                "this means ConfigureAwait(false) was used before an ObservableCollection mutation.");
    }
}
