using System;
using System.Collections.Generic;

namespace RimMind.Application.Common.Helpers
{
    /// <summary>
    /// Thread-safe per-key game-tick cooldown gate with bounded capacity and save-reload tick rollback awareness.
    /// </summary>
    public sealed class KeyedCooldownGate<TKey> where TKey : notnull
    {
        private readonly object _sync = new object();
        private readonly Dictionary<TKey, int> _lastTickByKey = new Dictionary<TKey, int>();
        private readonly int _maxEntries;

        public KeyedCooldownGate(int maxEntries = 256)
        {
            if (maxEntries <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxEntries), "Max entries must be positive.");
            _maxEntries = maxEntries;
        }

        public int MaxEntries => _maxEntries;

        public int Count
        {
            get
            {
                lock (_sync)
                {
                    return _lastTickByKey.Count;
                }
            }
        }

        /// <summary>
        /// Returns true if <paramref name="key"/> was recorded within <paramref name="cooldownTicks"/>
        /// and <paramref name="currentTick"/> has not rolled back (e.g., due to save reload).
        /// </summary>
        public bool IsOnCooldown(TKey key, int currentTick, int cooldownTicks)
        {
            if (cooldownTicks <= 0) return false;

            lock (_sync)
            {
                if (!_lastTickByKey.TryGetValue(key, out int lastTick))
                    return false;
                if (currentTick < lastTick)
                    return false;
                return (currentTick - lastTick) < cooldownTicks;
            }
        }

        /// <summary>
        /// Attempts to enter the gate for <paramref name="key"/>. If not on cooldown (or if
        /// <paramref name="currentTick"/> rolled back before the recorded tick after save reload),
        /// records <paramref name="currentTick"/>, prunes if over capacity, and returns <c>true</c>.
        /// Otherwise returns <c>false</c>.
        /// </summary>
        public bool TryEnter(TKey key, int currentTick, int cooldownTicks)
        {
            lock (_sync)
            {
                if (cooldownTicks > 0
                    && _lastTickByKey.TryGetValue(key, out int lastTick)
                    && currentTick >= lastTick
                    && (currentTick - lastTick) < cooldownTicks)
                {
                    return false;
                }

                _lastTickByKey[key] = currentTick;
                PruneIfNeededUnderLock(key, currentTick, cooldownTicks);
                return true;
            }
        }

        /// <summary>
        /// Unconditionally records <paramref name="currentTick"/> for <paramref name="key"/>
        /// and prunes if capacity is exceeded.
        /// </summary>
        public void Record(TKey key, int currentTick)
        {
            lock (_sync)
            {
                _lastTickByKey[key] = currentTick;
                PruneIfNeededUnderLock(key, currentTick, 0);
            }
        }

        public bool TryGetLastTick(TKey key, out int lastTick)
        {
            lock (_sync)
            {
                return _lastTickByKey.TryGetValue(key, out lastTick);
            }
        }

        public void Clear()
        {
            lock (_sync)
            {
                _lastTickByKey.Clear();
            }
        }

        private void PruneIfNeededUnderLock(TKey protectedKey, int currentTick, int cooldownTicks)
        {
            if (_lastTickByKey.Count <= _maxEntries) return;

            var comparer = EqualityComparer<TKey>.Default;
            var expiredOrRolledBack = new List<TKey>();
            foreach (var kvp in _lastTickByKey)
            {
                if (comparer.Equals(kvp.Key, protectedKey))
                    continue;
                if (currentTick < kvp.Value || (cooldownTicks > 0 && (currentTick - kvp.Value) >= cooldownTicks))
                {
                    expiredOrRolledBack.Add(kvp.Key);
                }
            }

            for (int i = 0; i < expiredOrRolledBack.Count; i++)
            {
                _lastTickByKey.Remove(expiredOrRolledBack[i]);
            }

            if (_lastTickByKey.Count <= _maxEntries) return;

            var ordered = new List<KeyValuePair<TKey, int>>(_lastTickByKey.Count);
            foreach (var kvp in _lastTickByKey)
            {
                if (!comparer.Equals(kvp.Key, protectedKey))
                {
                    ordered.Add(kvp);
                }
            }

            ordered.Sort((a, b) => a.Value.CompareTo(b.Value));
            int excess = _lastTickByKey.Count - _maxEntries;
            for (int i = 0; i < excess && i < ordered.Count; i++)
            {
                _lastTickByKey.Remove(ordered[i].Key);
            }
        }
    }
}
