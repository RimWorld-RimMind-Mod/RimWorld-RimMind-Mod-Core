using System;
using System.Collections.Generic;
using System.Linq;

namespace RimMind.Infrastructure.UI
{
    internal sealed class NpcSyncStateStore<TKey, TState>
        where TKey : notnull
    {
        private readonly int _capacity;
        private readonly Dictionary<TKey, Entry> _entries = new();
        private long _nextSequence;

        public NpcSyncStateStore(int capacity)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            _capacity = capacity;
        }

        public int Count => _entries.Count;

        public IReadOnlyList<TKey> Keys => _entries.Keys.ToList();

        public TState GetOrAdd(
            TKey key,
            Func<TState> factory,
            Func<TKey, bool> isActive,
            Action<TKey> cancelActive)
        {
            if (_entries.TryGetValue(key, out Entry? existing))
                return existing.State;

            while (_entries.Count >= _capacity)
            {
                KeyValuePair<TKey, Entry> oldest = _entries
                    .Where(pair => !isActive(pair.Key))
                    .OrderBy(pair => pair.Value.Sequence)
                    .FirstOrDefault();
                if (EqualityComparer<KeyValuePair<TKey, Entry>>.Default.Equals(oldest, default))
                {
                    oldest = _entries.OrderBy(pair => pair.Value.Sequence).First();
                    cancelActive(oldest.Key);
                }

                _entries.Remove(oldest.Key);
            }

            TState state = factory();
            _entries.Add(key, new Entry(state, ++_nextSequence));
            return state;
        }

        public bool TryGetValue(TKey key, out TState state)
        {
            if (_entries.TryGetValue(key, out Entry? entry))
            {
                state = entry.State;
                return true;
            }

            state = default!;
            return false;
        }

        public bool Remove(TKey key) => _entries.Remove(key);

        private sealed class Entry
        {
            public Entry(TState state, long sequence)
            {
                State = state;
                Sequence = sequence;
            }

            public TState State { get; }
            public long Sequence { get; }
        }
    }
}
