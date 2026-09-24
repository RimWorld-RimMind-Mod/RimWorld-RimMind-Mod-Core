using System;
using System.Collections.Generic;

namespace RimMind.Domain.ValueObjects
{
    public class KeyMeta
    {
        public string Key;
        public ContextLayer Layer;
        public float Priority;
        public Func<object, List<ContextEntry>> ValueProvider;
        public string OwnerMod;
        public CacheScope CacheScope;
        public string? OverrideSource;
        public bool IsIndexable;
        public float[]? KeyEmbedding;

        public ContextLayer OriginalLayer;
        public int UpdateCount;
        public float AdaptivePriority;
        public float CurrentScore;
        public float CurrentE;

        /// <summary>
        /// L1 async provider definition. Stored as object to avoid Domain->Application dependency.
        /// Runtime type is ContextProviderDef (Application layer). Null for legacy sync-only keys.
        /// </summary>
        public object? Def { get; set; }

        /// <summary>L2 recency: game tick when the provider value was last updated.</summary>
        public int LastUpdatedTick { get; set; }

        /// <summary>L2 cooldown: game tick when this key was last included in a snapshot.</summary>
        public int LastIncludedTick { get; set; }

        /// <summary>L2 query similarity: embedding of the last computed value for semantic dedup.</summary>
        public float[]? LastValueEmbedding { get; set; }

        public KeyMeta(string key, ContextLayer layer, float priority,
            Func<object, List<ContextEntry>> provider, string ownerMod,
            bool isIndexable = false, float[]? keyEmbedding = null,
            CacheScope cacheScope = CacheScope.Scenario)
        {
            Key = key;
            Layer = layer;
            OriginalLayer = layer;
            Priority = priority;
            ValueProvider = provider ?? (_ => new List<ContextEntry>());
            OwnerMod = ownerMod;
            IsIndexable = isIndexable;
            KeyEmbedding = keyEmbedding;
            CacheScope = cacheScope;
            AdaptivePriority = priority;
        }

        public float GetEffectivePriority()
        {
            return (Priority + AdaptivePriority) / 2f;
        }
    }
}
