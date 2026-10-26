using System;
using System.Threading;
using System.Threading.Tasks;

namespace RimMind.Application.Common.Threading
{
    /// <summary>
    /// Awaitable main-thread snapshot reads for code that runs on thread-pool threads
    /// (context providers, request pipeline stages, HTTP callbacks).
    /// </summary>
    /// <remarks>
    /// <para>Context providers (<c>ContextProviderDef.Provider</c>) are invoked from the request
    /// pipeline on thread-pool threads. Verse/Unity state (maps, pawns, needs, memories, relations,
    /// world components, <c>Translate()</c>) is mutated only on the Unity main thread and must not be
    /// enumerated there. Wrap every live-state read in <see cref="ReadAsync{T}"/>: the delegate builds an
    /// immutable snapshot (usually the final string) on the main thread and the provider awaits it.</para>
    /// <para>Semantics:</para>
    /// <list type="bullet">
    /// <item>Already on the main thread (per the injected predicate): the delegate runs inline.</item>
    /// <item>Otherwise the delegate is posted to the main-thread dispatch queue and awaited. The main
    /// thread never waits on the pipeline, so this cannot deadlock.</item>
    /// <item>If the main thread does not run it within <c>timeout</c> (long event, paused frame loop,
    /// minimized game), the call returns <c>fallback</c> and the posted delegate becomes a no-op.</item>
    /// <item>Cancellation of the caller's token throws <see cref="OperationCanceledException"/>
    /// (request-level outcome) and also turns the posted delegate into a no-op.</item>
    /// <item>An exception thrown by the delegate faults the returned task, so provider failures stay
    /// observable to the provider cache instead of silently disappearing.</item>
    /// </list>
    /// <para>The delegate must be short, read-only and bounded: it runs inside a frame.</para>
    /// </remarks>
    public sealed class MainThreadReader
    {
        /// <summary>Default wait used by the public API when the caller does not pass one.</summary>
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(3);

        private const int StatePending = 0;
        private const int StateRunning = 1;
        private const int StateAbandoned = 2;

        private readonly Action<Action> _post;
        private readonly Func<bool> _isMainThread;
        private long _timeoutCount;

        /// <param name="post">Queues an action for the main thread (never runs inline).</param>
        /// <param name="isMainThread">Returns true when the calling thread is the main thread.</param>
        public MainThreadReader(Action<Action> post, Func<bool> isMainThread)
        {
            _post = post ?? throw new ArgumentNullException(nameof(post));
            _isMainThread = isMainThread ?? throw new ArgumentNullException(nameof(isMainThread));
        }

        /// <summary>Number of reads that fell back because the main thread did not run them in time.</summary>
        public long TimeoutCount => Interlocked.Read(ref _timeoutCount);

        /// <summary>True when the calling thread is the main thread.</summary>
        public bool IsMainThread => _isMainThread();

        /// <summary>
        /// Runs <paramref name="read"/> on the main thread and returns its result, or
        /// <paramref name="fallback"/> when the main thread does not run it within <paramref name="timeout"/>.
        /// </summary>
        public Task<T> ReadAsync<T>(Func<T> read, T fallback, TimeSpan timeout, CancellationToken ct)
        {
            if (read == null)
                throw new ArgumentNullException(nameof(read));
            if (timeout <= TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
                throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be positive or infinite.");
            if (ct.IsCancellationRequested)
                return Task.FromCanceled<T>(ct);

            if (_isMainThread())
            {
                try
                {
                    return Task.FromResult(read());
                }
                catch (Exception ex)
                {
                    return Task.FromException<T>(ex);
                }
            }

            return PostAndAwaitAsync(read, fallback, timeout, ct);
        }

        private async Task<T> PostAndAwaitAsync<T>(Func<T> read, T fallback, TimeSpan timeout, CancellationToken ct)
        {
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            int state = StatePending;

            _post(() =>
            {
                // A caller that already timed out or was cancelled must not cost main-thread time.
                if (Interlocked.CompareExchange(ref state, StateRunning, StatePending) != StatePending)
                    return;
                try
                {
                    completion.TrySetResult(read());
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            });

            using (var delayCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                Task delay = Task.Delay(timeout, delayCancellation.Token);
                Task winner = await Task.WhenAny(completion.Task, delay).ConfigureAwait(false);
                if (winner == completion.Task)
                {
                    delayCancellation.Cancel();
                    return await completion.Task.ConfigureAwait(false);
                }
            }

            bool abandoned = Interlocked.CompareExchange(ref state, StateAbandoned, StatePending) == StatePending;
            if (!abandoned && completion.Task.IsCompleted)
                return await completion.Task.ConfigureAwait(false);

            ct.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _timeoutCount);
            return fallback;
        }
    }
}
