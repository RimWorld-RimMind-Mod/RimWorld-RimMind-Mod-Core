using System;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace RimMind.Application.Common.Helpers
{
    /// <summary>
    /// Thread-safe "recently not found" cache. Remembers lookup misses for a fixed
    /// real-time window so that repeated lookups of an unknown id do not rescan
    /// game state every call. Real time (not game ticks) is used so entries still
    /// expire while the game is paused and are unaffected by save reloads.
    /// </summary>
    public sealed class ExpiringNegativeCache<TKey> where TKey : notnull
    {
        private readonly ConcurrentDictionary<TKey, long> _missUntilMs = new();
        private readonly Func<long> _clockMs;
        private readonly long _ttlMs;
        private readonly int _maxEntries;

        /// <param name="ttl">How long a recorded miss suppresses rescans.</param>
        /// <param name="maxEntries">Soft cap; the cache is purged of expired entries (or cleared) when exceeded.</param>
        /// <param name="clockMs">Monotonic millisecond clock; defaults to <see cref="Stopwatch"/>.</param>
        public ExpiringNegativeCache(TimeSpan ttl, int maxEntries = 1024, Func<long>? clockMs = null)
        {
            if (ttl <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(ttl));
            if (maxEntries <= 0) throw new ArgumentOutOfRangeException(nameof(maxEntries));
            _ttlMs = (long)ttl.TotalMilliseconds;
            _maxEntries = maxEntries;
            _clockMs = clockMs ?? DefaultClock;
        }

        public int Count => _missUntilMs.Count;

        /// <summary>Returns true if <paramref name="key"/> missed recently and the miss has not expired.</summary>
        public bool IsRecentMiss(TKey key)
        {
            if (!_missUntilMs.TryGetValue(key, out long until)) return false;
            if (_clockMs() < until) return true;
            _missUntilMs.TryRemove(key, out _);
            return false;
        }

        /// <summary>Records a lookup miss for <paramref name="key"/>.</summary>
        public void RecordMiss(TKey key)
        {
            long now = _clockMs();
            if (_missUntilMs.Count >= _maxEntries) Purge(now);
            _missUntilMs[key] = now + _ttlMs;
        }

        /// <summary>Forgets a recorded miss (e.g. the id was just registered).</summary>
        public void Forget(TKey key) => _missUntilMs.TryRemove(key, out _);

        public void Clear() => _missUntilMs.Clear();

        private void Purge(long now)
        {
            foreach (var kvp in _missUntilMs)
            {
                if (kvp.Value <= now) _missUntilMs.TryRemove(kvp.Key, out _);
            }
            if (_missUntilMs.Count >= _maxEntries) _missUntilMs.Clear();
        }

        private static readonly Stopwatch SharedStopwatch = Stopwatch.StartNew();

        private static long DefaultClock() => SharedStopwatch.ElapsedMilliseconds;
    }
}
