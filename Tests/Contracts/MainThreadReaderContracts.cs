using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Threading;
using Xunit;

namespace RimMind.Tests.Contracts
{
    public sealed class MainThreadReaderContracts
    {
        [Fact]
        public async Task Read_on_main_thread_runs_inline_without_posting()
        {
            int posts = 0;
            int callerThread = Environment.CurrentManagedThreadId;
            int readThread = -1;
            var reader = new MainThreadReader(_ => Interlocked.Increment(ref posts), () => true);

            Task<string> task = reader.ReadAsync(() =>
            {
                readThread = Environment.CurrentManagedThreadId;
                return "snapshot";
            }, "fallback", TimeSpan.FromSeconds(1), CancellationToken.None);

            Assert.True(task.IsCompleted);
            Assert.Equal("snapshot", await task);
            Assert.Equal(callerThread, readThread);
            Assert.Equal(0, posts);
        }

        [Fact]
        public async Task Background_read_runs_on_draining_thread_and_returns_its_value()
        {
            var queue = new MainThreadDispatchQueue();
            using var main = new FakeMainThread(queue);
            var reader = new MainThreadReader(queue.Post, () => main.IsCurrent);

            int readThread = await Task.Run(() => reader.ReadAsync(
                () => Environment.CurrentManagedThreadId, -1, TimeSpan.FromSeconds(10), CancellationToken.None));

            Assert.Equal(main.ThreadId, readThread);
            Assert.Equal(0, reader.TimeoutCount);
        }

        [Fact]
        public async Task Timeout_returns_fallback_and_late_drain_skips_the_abandoned_read()
        {
            var queue = new MainThreadDispatchQueue();
            var reader = new MainThreadReader(queue.Post, () => false);
            int runs = 0;

            string result = await reader.ReadAsync(() =>
            {
                Interlocked.Increment(ref runs);
                return "late";
            }, "fallback", TimeSpan.FromMilliseconds(50), CancellationToken.None);

            Assert.Equal("fallback", result);
            Assert.Equal(1, reader.TimeoutCount);
            Assert.Equal(1, queue.Drain(ex => throw ex));
            Assert.Equal(0, runs);
        }

        [Fact]
        public async Task Cancellation_throws_and_late_drain_skips_the_read()
        {
            var queue = new MainThreadDispatchQueue();
            var reader = new MainThreadReader(queue.Post, () => false);
            using var cts = new CancellationTokenSource();
            int runs = 0;

            Task<string> task = reader.ReadAsync(() =>
            {
                Interlocked.Increment(ref runs);
                return "late";
            }, "fallback", TimeSpan.FromSeconds(30), cts.Token);
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
            queue.Drain(ex => throw ex);
            Assert.Equal(0, runs);
            Assert.Equal(0, reader.TimeoutCount);
        }

        [Fact]
        public async Task Read_exception_faults_the_task_instead_of_returning_fallback()
        {
            var queue = new MainThreadDispatchQueue();
            using var main = new FakeMainThread(queue);
            var reader = new MainThreadReader(queue.Post, () => main.IsCurrent);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Task.Run(() => reader.ReadAsync<string>(
                () => throw new InvalidOperationException("collection modified"),
                "fallback", TimeSpan.FromSeconds(10), CancellationToken.None)));

            Assert.Equal("collection modified", ex.Message);
        }

        [Fact]
        public async Task Concurrent_background_reads_all_run_on_main_thread_and_get_their_own_values()
        {
            const int readers = 200;
            var queue = new MainThreadDispatchQueue();
            using var main = new FakeMainThread(queue);
            var reader = new MainThreadReader(queue.Post, () => main.IsCurrent);
            int foreignRuns = 0;
            // Mutated only on the main thread, mirroring a live Verse collection.
            int mainOnlyCounter = 0;

            Task<(int Id, int Value)>[] tasks = Enumerable.Range(0, readers).Select(id => Task.Run(async () =>
            {
                int value = await reader.ReadAsync(() =>
                {
                    if (!main.IsCurrent) Interlocked.Increment(ref foreignRuns);
                    mainOnlyCounter++;
                    return id * 2;
                }, -1, TimeSpan.FromSeconds(10), CancellationToken.None);
                return (id, value);
            })).ToArray();

            var results = await Task.WhenAll(tasks);

            Assert.All(results, r => Assert.Equal(r.Id * 2, r.Value));
            Assert.Equal(0, foreignRuns);
            Assert.Equal(readers, mainOnlyCounter);
            Assert.Equal(0, reader.TimeoutCount);
        }

        [Fact]
        public void Rejects_non_positive_timeout()
        {
            var reader = new MainThreadReader(_ => { }, () => false);
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                { _ = reader.ReadAsync(() => 1, 0, TimeSpan.Zero, CancellationToken.None); });
        }

        /// <summary>Dedicated thread that drains the queue like the Unity frame loop.</summary>
        private sealed class FakeMainThread : IDisposable
        {
            private readonly Thread _thread;
            private volatile bool _stop;

            public FakeMainThread(MainThreadDispatchQueue queue)
            {
                using var started = new ManualResetEventSlim(false);
                _thread = new Thread(() =>
                {
                    ThreadId = Environment.CurrentManagedThreadId;
                    started.Set();
                    while (!_stop)
                    {
                        queue.Drain(null);
                        Thread.Sleep(1);
                    }
                }) { IsBackground = true };
                _thread.Start();
                started.Wait();
            }

            public int ThreadId { get; private set; }

            public bool IsCurrent => Environment.CurrentManagedThreadId == ThreadId;

            public void Dispose()
            {
                _stop = true;
                _thread.Join(TimeSpan.FromSeconds(5));
            }
        }
    }
}
