using System;

namespace RimMind.Domain.Llm
{
    /// <summary>
    /// Optional per-request scheduling, coalescing, TTL, and liveness policies applied by the request queue.
    /// </summary>
    public sealed class LlmRequestOptions
    {
        /// <summary>
        /// When set, any pending (not-yet-dispatched) queued request in the same mod queue with the same
        /// key is cancelled and superseded by this newer request.
        /// </summary>
        public string? CoalesceKey { get; init; }

        /// <summary>
        /// Maximum in-game ticks the request may wait in the queue before being dropped prior to dispatch.
        /// </summary>
        public int? MaxQueueWaitTicks { get; init; }

        /// <summary>
        /// Optional wall-clock UTC deadline after which a still-queued request is dropped prior to dispatch.
        /// </summary>
        public DateTime? ExpiresAtUtc { get; init; }

        /// <summary>
        /// Controls pre-dispatch colonist liveness verification for pawn-scoped requests (<c>NPC-*</c>).
        /// When <c>null</c>, colonist requests (<c>NpcId</c> starting with <c>NPC-</c>) automatically require
        /// an active living colonist unless explicitly opted out via <c>false</c>.
        /// </summary>
        public bool? RequireAliveColonist { get; init; }
    }
}
