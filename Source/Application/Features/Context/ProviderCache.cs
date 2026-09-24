using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Context;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Features.Context
{
    /// <summary>
    /// Staleness cache for async context providers. Caches provider results keyed by
    /// provider key plus the provider's declared cache scope identity, and respects
    /// staleness ticks and invalidation triggers.
    /// </summary>
    public sealed class ProviderCache
    {
        private readonly ConcurrentDictionary<CacheKey, CacheEntry> _entries = new();
        private readonly IAgentBus? _bus;
        private readonly ILogSink? _log;
        private readonly ITickProvider? _tickProvider;
        private readonly object _subscriptionSync = new object();
        private readonly ConcurrentDictionary<InvalidationSubscriptionKey, string> _invalidationSubscriptions = new();

        private readonly record struct CacheKey(string Key, CacheScope Scope, string ScopeIdentity);

        private readonly record struct CacheEntry(string? Value, int ComputedAtTicks, string NpcId, CacheScope Scope);

        private readonly record struct InvalidationSubscriptionKey(string ProviderKey, string EventName);

        public ProviderCache(IAgentBus? bus = null, ILogSink? log = null, ITickProvider? tickProvider = null)
        {
            _bus = bus;
            _log = log;
            _tickProvider = tickProvider;
        }

        /// <summary>
        /// Subscribe to invalidation triggers for a provider definition.
        /// Call this after registering a provider with InvalidationTriggers.
        /// </summary>
        public void SubscribeInvalidation(ContextProviderDef def)
        {
            lock (_subscriptionSync)
                SubscribeInvalidationUnsafe(def);
        }

        private void SubscribeInvalidationUnsafe(ContextProviderDef def)
        {
            if (_bus == null || def.InvalidationTriggers == null) return;
            foreach (var eventName in def.InvalidationTriggers)
            {
                if (string.IsNullOrWhiteSpace(eventName)) continue;

                var subscriptionKey = new InvalidationSubscriptionKey(def.Key, eventName);
                var busSubscriptionKey = _bus.SubscribeByName(eventName, _ => InvalidateKey(def.Key));
                if (!_invalidationSubscriptions.TryAdd(subscriptionKey, busSubscriptionKey))
                    _bus.Unsubscribe(busSubscriptionKey);
            }
        }

        public void ReplaceInvalidation(ContextProviderDef def)
        {
            lock (_subscriptionSync)
            {
                UnsubscribeInvalidationUnsafe(def.Key);
                SubscribeInvalidationUnsafe(def);
            }
        }

        public void UnsubscribeInvalidation(string providerKey)
        {
            lock (_subscriptionSync)
                UnsubscribeInvalidationUnsafe(providerKey);
        }

        private void UnsubscribeInvalidationUnsafe(string providerKey)
        {
            if (_bus == null) return;

            foreach (var subscription in _invalidationSubscriptions)
            {
                if (!string.Equals(subscription.Key.ProviderKey, providerKey, StringComparison.Ordinal)) continue;
                if (_invalidationSubscriptions.TryRemove(subscription.Key, out var busSubscriptionKey))
                    _bus.Unsubscribe(busSubscriptionKey);
            }
        }

        /// <summary>
        /// Get a cached value or compute it via the async provider.
        /// Respects staleness ticks: if the cached value is within the staleness window,
        /// it is returned directly without invoking the provider.
        /// </summary>
        public async Task<string?> GetOrComputeAsync(
            ContextProviderDef def,
            ProviderContext ctx,
            CancellationToken ct)
        {
            ProviderCacheResult result = await GetOrComputeWithOutcomeAsync(def, ctx, ct).ConfigureAwait(false);
            return result.Value;
        }

        /// <summary>
        /// Gets a cached provider result while preserving whether a provider failed.
        /// Callers that only need the historical null-on-failure behavior should use
        /// <see cref="GetOrComputeAsync"/>; layer builders use this result to keep
        /// provider failures observable without exposing exception details.
        /// </summary>
        internal async Task<ProviderCacheResult> GetOrComputeWithOutcomeAsync(
            ContextProviderDef def,
            ProviderContext ctx,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var cacheKey = new CacheKey(def.Key, def.CacheScope, GetScopeIdentity(def.CacheScope, ctx));
            var currentTicks = _tickProvider?.TicksGame ?? 0;

            // StalenessTicks == 0 means "no caching" — always call the provider.
            // StalenessTicks > 0 means "cache for this many ticks" — return cached if within window.
            if (def.StalenessTicks > 0 && _entries.TryGetValue(cacheKey, out var entry))
            {
                if (currentTicks - entry.ComputedAtTicks < def.StalenessTicks)
                {
                    return ProviderCacheResult.Succeeded(entry.Value);
                }
            }

            try
            {
                var value = await def.Provider(ctx, ct).ConfigureAwait(false);

                // Only cache if StalenessTicks > 0 (caching enabled)
                if (def.StalenessTicks > 0)
                {
                    _entries[cacheKey] = new CacheEntry(value, currentTicks, ctx.NpcId ?? string.Empty, def.CacheScope);
                }

                return ProviderCacheResult.Succeeded(value);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _log?.Warning($"[ProviderCache] Provider failed: key={def.Key}, exception={ex.GetType().Name}");
                return ProviderCacheResult.Failed();
            }
        }

        /// <summary>
        /// Invalidate all cache entries for a given key (across all NPCs and pawns).
        /// </summary>
        public void InvalidateKey(string key)
        {
            var keysToRemove = new List<CacheKey>();
            foreach (var kvp in _entries)
            {
                if (kvp.Key.Key == key)
                    keysToRemove.Add(kvp.Key);
            }
            foreach (var k in keysToRemove)
                _entries.TryRemove(k, out _);
        }

        /// <summary>
        /// Invalidate all cache entries for a given NPC.
        /// </summary>
        public void InvalidateNpc(string npcId)
        {
            var keysToRemove = new List<CacheKey>();
            foreach (var kvp in _entries)
            {
                if (kvp.Value.Scope == CacheScope.Pawn && kvp.Value.NpcId == npcId)
                    keysToRemove.Add(kvp.Key);
            }
            foreach (var k in keysToRemove)
                _entries.TryRemove(k, out _);
        }

        public void Clear()
        {
            _entries.Clear();
            lock (_subscriptionSync)
            {
                if (_bus == null)
                {
                    _invalidationSubscriptions.Clear();
                    return;
                }

                foreach (var subscription in _invalidationSubscriptions)
                {
                    if (_invalidationSubscriptions.TryRemove(subscription.Key, out var busSubscriptionKey))
                        _bus.Unsubscribe(busSubscriptionKey);
                }
            }
        }

        public int Count => _entries.Count;

        private static string GetScopeIdentity(CacheScope scope, ProviderContext ctx)
        {
            return scope switch
            {
                CacheScope.Static => "static",
                CacheScope.Pawn => ctx.PawnId != 0
                    ? "pawn:" + ctx.PawnId
                    : "pawn:npc:" + (ctx.NpcId ?? "trace:" + ctx.TraceId),
                CacheScope.Map => "map:" + (ctx.MapId?.ToString() ?? "none"),
                CacheScope.Storyteller => "storyteller",
                CacheScope.Scenario => "scenario:" + ctx.Scenario,
                _ => "scenario:" + ctx.Scenario
            };
        }

        internal readonly record struct ProviderCacheResult(string? Value, bool ProviderFaulted)
        {
            public static ProviderCacheResult Succeeded(string? value) => new(value, ProviderFaulted: false);
            public static ProviderCacheResult Failed() => new(null, ProviderFaulted: true);
        }
    }
}
