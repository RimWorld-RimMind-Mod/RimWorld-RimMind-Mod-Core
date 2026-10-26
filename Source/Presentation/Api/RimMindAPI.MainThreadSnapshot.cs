using System;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Threading;
using Verse;

namespace RimMind.Presentation.Api
{
    public static partial class RimMindAPI
    {
        /// <summary>
        /// Awaitable main-thread snapshot reads for context providers and other thread-pool code.
        /// </summary>
        /// <remarks>
        /// <para>Every <c>ContextProviderDef</c> provider runs on a thread-pool thread. Any read of live
        /// Verse state (Find.*, maps, pawns, needs, memories, relations, world/game components,
        /// third-party collections, <c>Translate()</c>) must be wrapped:</para>
        /// <code>
        /// provider: (pctx, ct) =&gt; RimMindAPI.MainThreadSnapshot.GetAsync(() =&gt; BuildText(pctx.PawnId), null, ct: ct)
        /// </code>
        /// <para>The delegate runs inline when already on the main thread; otherwise it is posted via
        /// <see cref="MainThread.Post"/> and awaited. If the main thread does not run it within the timeout
        /// (default <see cref="MainThreadReader.DefaultTimeout"/>, e.g. during a long event) the fallback is
        /// returned. Delegate exceptions fault the task. See <see cref="MainThreadReader"/>.</para>
        /// <para>Never block (<c>.Result</c>/<c>Wait()</c>) on the returned task from the main thread.</para>
        /// </remarks>
        public static class MainThreadSnapshot
        {
            private static readonly MainThreadReader Reader =
                new MainThreadReader(MainThread.Post, () => UnityData.IsInMainThread);

            /// <summary>True when the calling thread is the Unity main thread.</summary>
            public static bool IsMainThread => Reader.IsMainThread;

            /// <summary>Number of snapshot reads that fell back because the main thread did not run them in time.</summary>
            public static long TimeoutCount => Reader.TimeoutCount;

            /// <summary>
            /// Runs <paramref name="readOnMainThread"/> on the main thread and returns its result,
            /// or <paramref name="fallback"/> on timeout. Throws <see cref="OperationCanceledException"/>
            /// when <paramref name="ct"/> is cancelled.
            /// </summary>
            public static Task<T> GetAsync<T>(
                Func<T> readOnMainThread,
                T fallback = default!,
                TimeSpan? timeout = null,
                CancellationToken ct = default)
                => Reader.ReadAsync(readOnMainThread, fallback, timeout ?? MainThreadReader.DefaultTimeout, ct);
        }
    }
}
