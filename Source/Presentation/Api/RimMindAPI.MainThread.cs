using System;
using RimMind.Application.Common.Threading;
using RimMind.Domain.ValueObjects;

namespace RimMind.Presentation.Api
{
    public static partial class RimMindAPI
    {
        /// <summary>
        /// Thread-safe hand-off to the Unity main thread for all RimMind modules.
        /// </summary>
        /// <remarks>
        /// <para>Use this instead of <c>LongEventHandler.ExecuteWhenFinished</c> from background
        /// threads (Task.Run bodies, HTTP/stream callbacks, timer or cancellation callbacks).
        /// Vanilla's queue is an unsynchronized list and must only be touched on the main thread.</para>
        /// <para>Actions are always queued, never run inline, even when <see cref="Post"/> is
        /// called on the main thread. Core drains the queue from a <c>Root.Update</c> postfix,
        /// which runs every frame both at the main menu and in-game; draining pauses while a
        /// long event (loading/saving) is active, matching <c>ExecuteWhenFinished</c> timing.
        /// Exceptions thrown by an action are logged and do not affect other actions.</para>
        /// <para>Callers remain responsible for runtime-generation / window-lifetime checks
        /// inside the action, because it may run after the originating object is gone.</para>
        /// </remarks>
        public static class MainThread
        {
            private static readonly MainThreadDispatchQueue Queue = new MainThreadDispatchQueue();

            /// <summary>Queues <paramref name="action"/> to run on the main thread. Safe from any thread.</summary>
            public static void Post(Action action) => Queue.Post(action);

            /// <summary>Number of actions waiting for the next main-thread drain.</summary>
            public static int PendingCount => Queue.PendingCount;

            /// <summary>Core-owned per-frame drain; must only be called on the Unity main thread.</summary>
            internal static int DrainOnMainThread()
                => Queue.Drain(ex => RimMindErrors.Error("[RimMind-Core] Main-thread dispatched action failed.", ex));
        }
    }
}
