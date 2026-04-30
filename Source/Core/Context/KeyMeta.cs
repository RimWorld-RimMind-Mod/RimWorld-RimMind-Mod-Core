using System;
using System.Collections.Generic;
using Verse;

namespace RimMind.Core.Context
{
    public class KeyMeta
    {
        public string Key;
        public ContextLayer Layer;
        public ContextLayer OriginalLayer;
        public float InitialPriority;
        public float AdaptivePriority;
        public float CurrentE;
        public float CurrentScore;
        public int UpdateCount;
        public Func<Pawn, List<ContextEntry>> ValueProvider;
        public bool IsIndexable;
        public float[]? KeyEmbedding;
        public int Version;
        public string OwnerMod;

        public KeyMeta(string key, ContextLayer layer, float priority,
            Func<Pawn, List<ContextEntry>> provider, string ownerMod,
            bool isIndexable = false, float[]? keyEmbedding = null)
        {
            Key = key;
            Layer = layer;
            OriginalLayer = layer;
            InitialPriority = priority;
            AdaptivePriority = priority;
            ValueProvider = provider;
            OwnerMod = ownerMod;
            IsIndexable = isIndexable;
            KeyEmbedding = keyEmbedding;
            UpdateCount = 0;
            Version = 0;
        }

        public float GetEffectivePriority()
        {
            return UpdateCount > 0 ? AdaptivePriority : InitialPriority;
        }
    }
}
