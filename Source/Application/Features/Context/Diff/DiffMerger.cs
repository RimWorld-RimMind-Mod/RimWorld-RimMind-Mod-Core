using System.Collections.Generic;
using RimMind.Application.Common.Interfaces.Context;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Features.Context.Diff
{
    /// <summary>
    /// Merges expired diffs back into the baseline, compacting the diff store.
    /// </summary>
    internal sealed class DiffMerger
    {
        /// <summary>
        /// Merge expired diffs for an NPC back into the cache baseline.
        /// Currently a placeholder - will be fully implemented when async diff tracking is enabled.
        /// </summary>
        public void MergeExpired(string npcId, List<ContextDiff> expiredDiffs, IContextCacheManager cacheManager)
        {
            if (expiredDiffs == null || expiredDiffs.Count == 0) return;

            // IContextCacheManager operates on the entire L1 block cache per NPC.
            // Load current blocks, apply merged values, then write back.
            if (!cacheManager.TryGetL1BlockCache(npcId, out var blocks))
                blocks = new Dictionary<string, string>();

            foreach (var diff in expiredDiffs)
            {
                if (!string.IsNullOrEmpty(diff.NewValue))
                {
                    blocks[diff.Key] = diff.NewValue;
                }
                else
                {
                    blocks.Remove(diff.Key);
                }
            }

            cacheManager.SetL1BlockCache(npcId, blocks);
        }
    }
}
