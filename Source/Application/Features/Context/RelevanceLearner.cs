using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Context;
using RimMind.Application.Features.Utility;

namespace RimMind.Application.Features.Context
{
    public sealed class RelevanceLearner : IRelevanceLearner
    {
        private readonly ConcurrentDictionary<(string Scenario, string Key), RingBuffer<bool>> _stats = new();
        private readonly ConcurrentDictionary<string, (string Scenario, IReadOnlyList<string> Keys, int TimestampTick)> _pendingTraces = new();
        private const int RingSize = 100;

        private readonly ITickProvider? _tickProvider;
        private int _lastCleanupTick = 0;
        private const int CleanupIntervalTicks = 600;   // ~10 seconds
        private const int TraceTimeoutTicks = 36000;    // ~10 minutes

        public RelevanceLearner(ITickProvider? tickProvider = null)
        {
            _tickProvider = tickProvider;
        }

        public void RecordInclusion(string traceId, string scenario, IReadOnlyList<string> includedKeys)
        {
            if (string.IsNullOrEmpty(traceId) || includedKeys == null) return;
            MaybeCleanup();
            var nowTick = _tickProvider?.TicksGame ?? 0;
            _pendingTraces[traceId] = (scenario, includedKeys, nowTick);
        }

        public void RecordUsage(string traceId, IReadOnlyList<string> usedKeys)
        {
            if (string.IsNullOrEmpty(traceId)) return;
            if (!_pendingTraces.TryRemove(traceId, out var entry)) return;

            var usedSet = usedKeys != null ? new HashSet<string>(usedKeys) : new HashSet<string>();

            foreach (var key in entry.Keys)
            {
                var ring = _stats.GetOrAdd((entry.Scenario, key), _ => new RingBuffer<bool>(RingSize));
                ring.Add(usedSet.Contains(key));
            }
        }

        public float GetFeedbackScore(string scenario, string key)
        {
            if (!_stats.TryGetValue((scenario, key), out var ring)) return 0.5f;  // unknown -> neutral
            if (ring.Count == 0) return 0.5f;
            int usedCount = ring.AsEnumerable().Count(b => b);
            return usedCount / (float)ring.Count;
        }

        private void MaybeCleanup()
        {
            var now = _tickProvider?.TicksGame ?? 0;
            if (now == 0) return;  // no tick provider, can't determine age
            if (now - _lastCleanupTick < CleanupIntervalTicks) return;
            _lastCleanupTick = now;

            var expired = _pendingTraces
                .Where(kvp => now - kvp.Value.TimestampTick > TraceTimeoutTicks)
                .Select(kvp => kvp.Key)
                .ToList();
            foreach (var id in expired)
                _pendingTraces.TryRemove(id, out _);
        }
    }
}
