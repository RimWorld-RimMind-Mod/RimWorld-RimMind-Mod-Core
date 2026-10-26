using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Threading;
using RimMind.Domain.ValueObjects;
using RimMind.Presentation.Api;
using Xunit;

namespace RimMind.Tests.Contracts
{
    public sealed class MainThreadDispatchContracts
    {
        [Fact]
        public async Task Concurrent_posts_from_many_threads_each_run_exactly_once_on_the_draining_thread_in_per_producer_order()
        {
            const int producers = 16;
            const int postsPerProducer = 500;
            var queue = new MainThreadDispatchQueue();
            var runs = new ConcurrentDictionary<(int Producer, int Seq), int>();
            var lastSeqByProducer = new int[producers];
            for (int i = 0; i < producers; i++) lastSeqByProducer[i] = -1;
            int outOfOrder = 0;
            int drainThreadId = Environment.CurrentManagedThreadId;
            int foreignThreadRuns = 0;
            using var start = new ManualResetEventSlim(false);

            Task[] tasks = Enumerable.Range(0, producers).Select(producer => Task.Run(() =>
            {
                start.Wait();
                for (int seq = 0; seq < postsPerProducer; seq++)
                {
                    int capturedSeq = seq;
                    queue.Post(() =>
                    {
                        runs.AddOrUpdate((producer, capturedSeq), 1, (_, count) => count + 1);
                        if (Environment.CurrentManagedThreadId != drainThreadId)
                            Interlocked.Increment(ref foreignThreadRuns);
                        if (capturedSeq <= lastSeqByProducer[producer])
                            outOfOrder++;
                        lastSeqByProducer[producer] = capturedSeq;
                    });
                }
            })).ToArray();

            start.Set();
            int executed = 0;
            // Drain concurrently with producers, like a frame loop racing background work.
            while (!tasks.All(t => t.IsCompleted))
                executed += queue.Drain(ex => throw ex);
            await Task.WhenAll(tasks);
            executed += queue.Drain(ex => throw ex);

            Assert.Equal(producers * postsPerProducer, executed);
            Assert.Equal(producers * postsPerProducer, runs.Count);
            Assert.All(runs.Values, count => Assert.Equal(1, count));
            Assert.Equal(0, outOfOrder);
            Assert.Equal(0, foreignThreadRuns);
            Assert.Equal(0, queue.PendingCount);
        }

        [Fact]
        public void A_throwing_action_is_reported_and_does_not_stop_later_actions()
        {
            var queue = new MainThreadDispatchQueue();
            var order = new List<string>();
            var errors = new List<Exception>();

            queue.Post(() => order.Add("first"));
            queue.Post(() => throw new InvalidOperationException("boom"));
            queue.Post(() => order.Add("third"));

            int executed = queue.Drain(errors.Add);

            Assert.Equal(3, executed);
            Assert.Equal(new[] { "first", "third" }, order);
            InvalidOperationException error = Assert.IsType<InvalidOperationException>(Assert.Single(errors));
            Assert.Equal("boom", error.Message);

            // A failing error handler must not break the loop either.
            queue.Post(() => throw new InvalidOperationException("again"));
            queue.Post(() => order.Add("after-handler-failure"));
            queue.Drain(_ => throw new Exception("handler failed"));
            Assert.Equal("after-handler-failure", order.Last());
        }

        [Fact]
        public void Post_never_runs_inline_and_reposts_during_drain_wait_for_the_next_drain()
        {
            var queue = new MainThreadDispatchQueue();
            var order = new List<string>();

            queue.Post(() =>
            {
                order.Add("outer");
                queue.Post(() => order.Add("follow-up"));
            });
            Assert.Empty(order);

            Assert.Equal(1, queue.Drain(null));
            Assert.Equal(new[] { "outer" }, order);
            Assert.Equal(1, queue.PendingCount);

            Assert.Equal(1, queue.Drain(null));
            Assert.Equal(new[] { "outer", "follow-up" }, order);
            Assert.Throws<ArgumentNullException>(() => queue.Post(null!));
        }

        [Fact]
        public async Task Public_api_runs_background_posts_on_core_drain_and_routes_failures_to_rimmind_errors()
        {
            Action<string, Exception?>? previousOnError = RimMindErrors.OnError;
            var reported = new ConcurrentQueue<Exception?>();
            RimMindErrors.OnError = (_, ex) => reported.Enqueue(ex);
            try
            {
                var marker = new InvalidOperationException("dispatched failure");
                int ranOnThread = -1;
                await Task.Run(() =>
                {
                    RimMindAPI.MainThread.Post(() => ranOnThread = Environment.CurrentManagedThreadId);
                    RimMindAPI.MainThread.Post(() => throw marker);
                });
                Assert.Equal(-1, ranOnThread);

                RimMindAPI.MainThread.DrainOnMainThread();

                Assert.Equal(Environment.CurrentManagedThreadId, ranOnThread);
                Assert.Contains(marker, reported);
            }
            finally
            {
                RimMindErrors.OnError = previousOnError;
            }
        }
    }
}
