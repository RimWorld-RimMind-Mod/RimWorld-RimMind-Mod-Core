using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Features.Llm
{
    /// <summary>
    /// Reusable thread-safe multi-endpoint failover and circuit-isolation policy.
    /// Shared by Core's built-in client and extension model services.
    /// </summary>
    public sealed class EndpointFailoverPolicy
    {
        public const long DefaultIsolationMs = 180_000L;
        public const long DefaultTransientRateLimitIsolationMs = 2_000L;
        public const long DefaultPermanentIsolationMs = 1_800_000L;

        private readonly ConcurrentDictionary<string, long> _isolatedUntilMs = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, long> _exhaustedUntilMs = new(StringComparer.OrdinalIgnoreCase);
        private readonly Func<long> _clockMs;
        private readonly long _defaultIsolationMs;
        private readonly long _transientRateLimitIsolationMs;
        private readonly long _permanentIsolationMs;

        public EndpointFailoverPolicy(
            Func<long>? clockMs = null,
            long defaultIsolationMs = DefaultIsolationMs,
            long transientRateLimitIsolationMs = DefaultTransientRateLimitIsolationMs,
            long permanentIsolationMs = DefaultPermanentIsolationMs)
        {
            _clockMs = clockMs ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            _defaultIsolationMs = defaultIsolationMs;
            _transientRateLimitIsolationMs = transientRateLimitIsolationMs;
            _permanentIsolationMs = permanentIsolationMs;
        }

        public bool IsIsolated(string nodeKey, long? nowMs = null)
        {
            if (string.IsNullOrEmpty(nodeKey)) return false;
            long now = nowMs ?? _clockMs();
            return _isolatedUntilMs.TryGetValue(nodeKey, out long untilMs) && now < untilMs;
        }

        public bool IsPermanentlyExhausted(string nodeKey, long? nowMs = null)
        {
            if (string.IsNullOrEmpty(nodeKey)) return false;
            long now = nowMs ?? _clockMs();
            return _exhaustedUntilMs.TryGetValue(nodeKey, out long untilMs) && now < untilMs;
        }

        public void RecordSuccess(string nodeKey)
        {
            if (string.IsNullOrEmpty(nodeKey)) return;
            _isolatedUntilMs.TryRemove(nodeKey, out _);
            _exhaustedUntilMs.TryRemove(nodeKey, out _);
        }

        public void RecordFailure(string nodeKey, long cooldownMs, bool isPermanentQuotaOrAuth = false, long? nowMs = null)
        {
            if (string.IsNullOrEmpty(nodeKey)) return;
            long now = nowMs ?? _clockMs();
            _isolatedUntilMs[nodeKey] = now + cooldownMs;
            if (isPermanentQuotaOrAuth)
            {
                _exhaustedUntilMs[nodeKey] = now + cooldownMs;
            }
        }

        public void RecordFailure(string nodeKey, RimMindError error, bool isRateLimited = false, long? nowMs = null)
        {
            if (string.IsNullOrEmpty(nodeKey)) return;
            // Content moderation and permanent request schema errors must NOT isolate endpoints
            if (error.Code == RimMindErrorCode.ContentFiltered || error.Code == RimMindErrorCode.Cancelled)
                return;

            if (error.Code == RimMindErrorCode.ClientQuotaOrAuthFailure)
            {
                RecordFailure(nodeKey, _permanentIsolationMs, isPermanentQuotaOrAuth: true, nowMs: nowMs);
            }
            else if (isRateLimited)
            {
                RecordFailure(nodeKey, _transientRateLimitIsolationMs, isPermanentQuotaOrAuth: false, nowMs: nowMs);
            }
            else
            {
                RecordFailure(nodeKey, _defaultIsolationMs, isPermanentQuotaOrAuth: false, nowMs: nowMs);
            }
        }

        public List<T> OrderCandidates<T>(IReadOnlyList<T> candidates, Func<T, string> keySelector, long? nowMs = null)
        {
            if (candidates == null || candidates.Count == 0)
                return new List<T>();
            if (candidates.Count == 1)
                return new List<T> { candidates[0] };

            long now = nowMs ?? _clockMs();
            var nonExhausted = candidates.Where(n => !IsPermanentlyExhausted(keySelector(n), now)).ToList();
            var pool = nonExhausted.Count > 0 ? nonExhausted : candidates.ToList();

            var healthy = new List<T>(pool.Count);
            var isolated = new List<(T Node, long UntilMs)>();

            for (int i = 0; i < pool.Count; i++)
            {
                var node = pool[i];
                string key = keySelector(node);
                if (_isolatedUntilMs.TryGetValue(key, out long untilMs) && now < untilMs)
                {
                    isolated.Add((node, untilMs));
                }
                else
                {
                    healthy.Add(node);
                }
            }

            foreach (var item in isolated.OrderBy(x => x.UntilMs))
            {
                healthy.Add(item.Node);
            }

            return healthy;
        }

        public void Reset()
        {
            _isolatedUntilMs.Clear();
            _exhaustedUntilMs.Clear();
        }
    }
}
