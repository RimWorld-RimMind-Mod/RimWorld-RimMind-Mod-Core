using System.Collections.Generic;
using RimMind.Domain.Interfaces;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Common.Interfaces.Context
{
    public interface IContextCacheManager
    {
        IReadOnlyDictionary<string, ChatMessage> L0Cache { get; }
        IReadOnlyDictionary<string, Dictionary<string, string>> L1BlockCache { get; }
        IReadOnlyDictionary<string, int> L1Version { get; }
        IReadOnlyDictionary<string, Dictionary<string, int>> L1KeyVersions { get; }
        IReadOnlyDictionary<string, bool> PendingCacheEvents { get; }
        IEmbedCache EmbedCache { get; }

        void TouchCache(string cacheKey);
        void RemoveL0CacheForNpc(string npcId);
        void InvalidateLayer(string npcId, ContextLayer layer);
        void InvalidateKey(string npcId, string key);
        void UpdateBaseline(string npcId);
        void InvalidateNpc(string npcId);
        void Reset();
        int GetL0CacheCount();
        int GetL1BlockCacheCount();
        int GetEmbedCacheCount();
        void ClearPendingCacheEvents();

        bool TryGetL0CacheItem(string key, out ChatMessage msg);
        void SetL0CacheItem(string key, ChatMessage msg);
        bool RemoveL0CacheItem(string key);
        bool TryGetL1BlockCache(string npcId, out Dictionary<string, string> blocks);
        void SetL1BlockCache(string npcId, Dictionary<string, string> blocks);
        bool TryGetL1Version(string npcId, out int version);
        void SetL1Version(string npcId, int version);
        bool TryGetL1KeyVersions(string npcId, out Dictionary<string, int> versions);
        void SetL1KeyVersions(string npcId, Dictionary<string, int> versions);
        bool TryGetPendingCacheEvent(string key, out bool value);
        void SetPendingCacheEvent(string key, bool value);
    }
}
