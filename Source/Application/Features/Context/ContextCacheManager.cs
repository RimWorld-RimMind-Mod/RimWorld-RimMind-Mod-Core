using System.Collections.Concurrent;
using System.Collections.Generic;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Context;
using RimMind.Domain.Interfaces;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Features.Context
{
    public sealed class ContextCacheManager : IContextCacheManager
    {
        private readonly ConcurrentDictionary<string, ChatMessage> _l0Cache
            = new ConcurrentDictionary<string, ChatMessage>();
        private readonly ConcurrentDictionary<string, Dictionary<string, string>> _l1BlockCache
            = new ConcurrentDictionary<string, Dictionary<string, string>>();
        // Protects replacement of inner Dictionary<string,string> in _l1BlockCache.
        // Mirrors the lock(versions) pattern used by _l1KeyVersions in InvalidateKey,
        // so that Set/TryGet are mutually exclusive and TryGet returns a snapshot
        // instead of a direct reference to the internal mutable Dictionary.
        private readonly object _l1BlockCacheLock = new object();
        private readonly ConcurrentDictionary<string, int> _l1Version
            = new ConcurrentDictionary<string, int>();
        private readonly ConcurrentDictionary<string, Dictionary<string, int>> _l1KeyVersions
            = new ConcurrentDictionary<string, Dictionary<string, int>>();
        private readonly ConcurrentDictionary<string, bool> _pendingCacheEvents
            = new ConcurrentDictionary<string, bool>();
        private readonly IEmbedCache _embedCache;
        private readonly ILogSink? _log;

        public ContextCacheManager(ILogSink? log = null, IEmbedCache? embedCache = null)
        {
            _log = log;
            _embedCache = embedCache!;
        }

        public IReadOnlyDictionary<string, ChatMessage> L0Cache => _l0Cache;
        public IReadOnlyDictionary<string, Dictionary<string, string>> L1BlockCache => _l1BlockCache;
        public IReadOnlyDictionary<string, int> L1Version => _l1Version;
        public IReadOnlyDictionary<string, Dictionary<string, int>> L1KeyVersions => _l1KeyVersions;
        public IReadOnlyDictionary<string, bool> PendingCacheEvents => _pendingCacheEvents;
        public IEmbedCache EmbedCache => _embedCache;

        public void TouchCache(string cacheKey) { /* Intentionally empty: reserved for future LRU cache eviction */ }
        public void RemoveL0CacheForNpc(string npcId)
        {
            foreach (var key in _l0Cache.Keys)
            {
                if (key.StartsWith(npcId + ":")) _l0Cache.TryRemove(key, out _);
            }
        }
        public void InvalidateLayer(string npcId, ContextLayer layer)
        {
            _l1BlockCache.TryRemove(npcId, out _);
            _l1Version.AddOrUpdate(npcId, 0, (_, __) => 0);
        }
        public void InvalidateKey(string npcId, string key)
        {
            if (_l1KeyVersions.TryGetValue(npcId, out var versions))
            {
                lock (versions) { versions.Remove(key); }
            }
        }
        public void UpdateBaseline(string npcId) { /* Intentionally empty: reserved for future baseline auto-refresh */ }
        public void InvalidateNpc(string npcId)
        {
            RemoveL0CacheForNpc(npcId);
            _l1BlockCache.TryRemove(npcId, out _);
            _l1Version.TryRemove(npcId, out _);
            _l1KeyVersions.TryRemove(npcId, out _);
        }
        public void Reset()
        {
            _l0Cache.Clear(); _l1BlockCache.Clear(); _l1Version.Clear();
            _l1KeyVersions.Clear(); _pendingCacheEvents.Clear(); _embedCache.Clear();
        }
        public int GetL0CacheCount() => _l0Cache.Count;
        public int GetL1BlockCacheCount() => _l1BlockCache.Count;
        public int GetEmbedCacheCount() => _embedCache.Count;
        public void ClearPendingCacheEvents() => _pendingCacheEvents.Clear();

        public bool TryGetL0CacheItem(string key, out ChatMessage msg) => _l0Cache.TryGetValue(key, out msg!);
        public void SetL0CacheItem(string key, ChatMessage msg) => _l0Cache[key] = msg;
        public bool RemoveL0CacheItem(string key) => _l0Cache.TryRemove(key, out _);
        public bool TryGetL1BlockCache(string npcId, out Dictionary<string, string> blocks)
        {
            lock (_l1BlockCacheLock)
            {
                if (_l1BlockCache.TryGetValue(npcId, out var inner))
                {
                    // Return a snapshot so callers cannot mutate the internal Dictionary.
                    // Callers that need to update must use SetL1BlockCache to write back.
                    blocks = new Dictionary<string, string>(inner);
                    return true;
                }
                blocks = null!;
                return false;
            }
        }
        public void SetL1BlockCache(string npcId, Dictionary<string, string> blocks)
        {
            lock (_l1BlockCacheLock)
            {
                _l1BlockCache[npcId] = blocks;
            }
        }
        public bool TryGetL1Version(string npcId, out int version) => _l1Version.TryGetValue(npcId, out version);
        public void SetL1Version(string npcId, int version) => _l1Version[npcId] = version;
        public bool TryGetL1KeyVersions(string npcId, out Dictionary<string, int> versions) => _l1KeyVersions.TryGetValue(npcId, out versions!);
        public void SetL1KeyVersions(string npcId, Dictionary<string, int> versions) => _l1KeyVersions[npcId] = versions;
        public bool TryGetPendingCacheEvent(string key, out bool value) => _pendingCacheEvents.TryGetValue(key, out value);
        public void SetPendingCacheEvent(string key, bool value) => _pendingCacheEvents[key] = value;
    }
}
