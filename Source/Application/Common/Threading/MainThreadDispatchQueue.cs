using System;
using System.Collections.Concurrent;

namespace RimMind.Application.Common.Threading
{
    /// <summary>
    /// Thread-safe FIFO hand-off from background threads to a single consumer thread.
    /// Producers call <see cref="Post"/> from any thread; the owning runtime calls
    /// <see cref="Drain"/> once per frame on the Unity main thread.
    /// </summary>
    /// <remarks>
    /// <para><see cref="Post"/> never runs the action inline, even when called on the consumer
    /// thread; actions always run on the next <see cref="Drain"/>. This keeps ordering
    /// deterministic and avoids re-entrancy into UI code that is currently drawing.</para>
    /// <para><see cref="Drain"/> only runs the actions that were queued when it started, so an
    /// action that posts another action cannot starve the frame; the follow-up runs next drain.
    /// An exception thrown by one action is reported to the error callback and does not stop
    /// the remaining actions.</para>
    /// </remarks>
    public sealed class MainThreadDispatchQueue
    {
        private readonly ConcurrentQueue<Action> _pending = new ConcurrentQueue<Action>();

        /// <summary>Number of actions waiting for the next drain.</summary>
        public int PendingCount => _pending.Count;

        /// <summary>Queues <paramref name="action"/> for the consumer thread. Safe from any thread.</summary>
        public void Post(Action action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            _pending.Enqueue(action);
        }

        /// <summary>
        /// Runs, in posting order, every action that was queued when the drain started.
        /// Must be called from the consumer (main) thread only.
        /// </summary>
        /// <param name="onError">Receives exceptions thrown by individual actions.</param>
        /// <returns>The number of actions that were executed (including ones that threw).</returns>
        public int Drain(Action<Exception>? onError)
        {
            int budget = _pending.Count;
            int executed = 0;
            while (executed < budget && _pending.TryDequeue(out Action? action))
            {
                executed++;
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    try
                    {
                        onError?.Invoke(ex);
                    }
                    catch
                    {
                        // Error reporting must never break the drain loop.
                    }
                }
            }

            return executed;
        }
    }
}
