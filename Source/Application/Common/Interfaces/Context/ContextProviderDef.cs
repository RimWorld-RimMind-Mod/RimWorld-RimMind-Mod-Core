using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Common.Interfaces.Context
{
    /// <summary>
    /// Definition of an async context provider with staleness and invalidation support.
    /// Replaces the old Func&lt;object, List&lt;ContextEntry&gt;&gt; synchronous provider pattern.
    /// </summary>
    public sealed class ContextProviderDef
    {
        public string Key { get; }
        public ContextLayer Layer { get; }
        public float Priority { get; }
        public string? OwnerMod { get; }

        public CacheScope CacheScope { get; }

        /// <summary>
        /// Async provider function. Receives ProviderContext + CancellationToken.
        /// Returns null if the provider has no content for this context.
        /// </summary>
        public Func<ProviderContext, CancellationToken, Task<string?>> Provider { get; }

        /// <summary>
        /// Staleness window in game ticks. If 0, provider is called every time.
        /// If > 0, cached result is reused until this many ticks have passed.
        /// </summary>
        public int StalenessTicks { get; }

        /// <summary>
        /// AgentBus event type names that trigger cache invalidation for this key.
        /// </summary>
        public IReadOnlyList<string>? InvalidationTriggers { get; }

        /// <summary>
        /// Whether the user can pin this key (force it to always be included).
        /// </summary>
        public bool AllowUserPin { get; }

        /// <summary>
        /// Whether this key's content is sensitive (for PII sanitization middleware).
        /// </summary>
        public bool IsSensitive { get; }

        public ContextProviderDef(
            string key,
            ContextLayer layer,
            float priority,
            Func<ProviderContext, CancellationToken, Task<string?>> provider,
            string? ownerMod = null,
            int stalenessTicks = 0,
            IReadOnlyList<string>? invalidationTriggers = null,
            bool allowUserPin = true,
            bool isSensitive = false,
            CacheScope cacheScope = CacheScope.Scenario)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Layer = layer;
            Priority = priority;
            Provider = provider ?? throw new ArgumentNullException(nameof(provider));
            OwnerMod = ownerMod;
            StalenessTicks = stalenessTicks;
            InvalidationTriggers = invalidationTriggers;
            AllowUserPin = allowUserPin;
            IsSensitive = isSensitive;
            CacheScope = cacheScope;
        }
    }

    /// <summary>
    /// Context passed to async providers. Contains only primitive types to avoid
    /// Application-layer dependency on Verse types (Pawn, Map).
    /// </summary>
    public sealed record ProviderContext
    {
        public string Scenario { get; init; }
        public string TraceId { get; init; }

        /// <summary>Pawn.thingIDNumber. 0 if no pawn is associated.</summary>
        public int PawnId { get; init; }

        public string? NpcId { get; init; }

        /// <summary>Map.uniqueID. null if no map is associated.</summary>
        public int? MapId { get; init; }

        public IReadOnlyDictionary<string, object?>? Hints { get; init; }

        public ProviderContext(string scenario, string traceId)
        {
            Scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));
            TraceId = traceId ?? throw new ArgumentNullException(nameof(traceId));
        }
    }
}
