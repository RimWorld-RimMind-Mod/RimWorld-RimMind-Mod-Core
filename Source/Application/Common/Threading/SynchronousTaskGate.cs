using System;
using System.Threading;
using System.Threading.Tasks;

namespace RimMind.Application.Common.Threading
{
    /// <summary>
    /// Main-thread guard for APIs that return <see cref="Task"/> but are consumed synchronously
    /// (tool handlers, mechanism writes). A completed task yields its result exactly as before;
    /// an incomplete task is never blocked on — blocking the Unity main thread can deadlock with
    /// continuations that need the main thread, and resuming on the thread pool would touch Verse
    /// state off-thread. Callers turn a <c>false</c> return into a failed result instead.
    /// </summary>
    public static class SynchronousTaskGate
    {
        public const string AsyncNotSupportedMessage =
            "tool returned asynchronously; not supported on main thread";

        /// <summary>
        /// Returns <c>true</c> and the task result when <paramref name="task"/> has already completed
        /// (faults and cancellations are rethrown exactly like <c>GetAwaiter().GetResult()</c>).
        /// Returns <c>false</c> without blocking when the task is still running; its eventual fault is
        /// observed so it cannot surface as an unobserved task exception.
        /// </summary>
        public static bool TryCompleteSynchronously<T>(Task<T> task, out T result)
            => TryGetCompletedResult(task, out result);

        public static bool TryGetCompletedResult<T>(Task<T> task, out T result)
        {
            if (task == null) throw new ArgumentNullException(nameof(task));

            if (!task.IsCompleted)
            {
                ObserveFault(task);
                result = default!;
                return false;
            }

            result = task.GetAwaiter().GetResult();
            return true;
        }

        /// <summary>Attaches a continuation that reads the exception of a faulted task.</summary>
        public static void ObserveFault(Task task)
        {
            if (task == null) return;
            task.ContinueWith(
                static t => { _ = t.Exception; },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        /// <summary>
        /// Returns <c>true</c> exactly once per <paramref name="flag"/> so warnings about asynchronous
        /// handlers are logged once instead of every agent tick.
        /// </summary>
        public static bool TryClaimLogOnce(ref int flag) => Interlocked.Exchange(ref flag, 1) == 0;
    }
}
