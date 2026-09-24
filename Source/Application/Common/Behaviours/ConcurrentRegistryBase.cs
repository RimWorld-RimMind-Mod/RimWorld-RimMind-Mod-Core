using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using RimMind.Application.Common.Interfaces.Registry;

namespace RimMind.Application.Common.Behaviours
{
    /// <summary>
    /// Base class for concurrent registries. Provides Register/Unregister/FindById/All/UnregisterByOwner.
    /// Subclasses provide key extractor and owner extractor via constructor.
    /// </summary>
    public abstract class ConcurrentRegistryBase<TKey, TValue> : IOwnedRegistry
        where TKey : notnull
        where TValue : class
    {
        protected readonly ConcurrentDictionary<TKey, TValue> _items = new ConcurrentDictionary<TKey, TValue>();
        private readonly Func<TValue, TKey> _keyExtractor;
        private readonly Func<TValue, string>? _ownerExtractor;

        protected ConcurrentRegistryBase(Func<TValue, TKey> keyExtractor, Func<TValue, string>? ownerExtractor = null)
        {
            _keyExtractor = keyExtractor ?? throw new ArgumentNullException(nameof(keyExtractor));
            _ownerExtractor = ownerExtractor;
        }

        public void Register(TValue item)
        {
            if (item == null) return;
            var key = _keyExtractor(item);
            _items.AddOrUpdate(key, item, (_, __) => item);
        }

        public bool Unregister(TKey key) => _items.TryRemove(key, out _);

        public TValue? FindById(TKey key) => _items.TryGetValue(key, out var v) ? v : null;

        public IReadOnlyList<TValue> All => new List<TValue>(_items.Values);

        /// <inheritdoc/>
        public int UnregisterByOwner(string ownerModId)
        {
            if (ownerModId == null) throw new ArgumentNullException(nameof(ownerModId));
            if (_ownerExtractor == null) return 0;
            var toRemove = new List<TKey>();
            foreach (var kvp in _items)
            {
                if (_ownerExtractor(kvp.Value) == ownerModId)
                    toRemove.Add(kvp.Key);
            }
            foreach (var k in toRemove) _items.TryRemove(k, out _);
            return toRemove.Count;
        }
    }
}
