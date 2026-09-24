using System.Collections.Generic;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Common.Interfaces.Context
{
    public interface IContextDiffTracker
    {
        IReadOnlyDictionary<string, List<ContextDiff>> DiffStore { get; }
        IReadOnlyDictionary<string, Dictionary<string, string>> KeyLastValues { get; }
        IReadOnlyDictionary<string, Dictionary<string, float>> KeyLastNumericValues { get; }

        void AddDiff(string npcId, string key, string oldValue, string newValue, ContextLayer layer);
        void MergeExpiredDiffs(string npcId, IContextCacheManager cacheManager);
        void UpdateKeyValues(string npcId, List<KeyMeta> keys, object? pawn, IContextCacheManager cacheManager, IBudgetScheduler scheduler);
        void StoreNumericValues(string npcId, Dictionary<string, float> values);
        void ClearNpcDiffs(string npcId);
        void RemoveNpcKeyLastValues(string npcId);
        void Reset();
        int GetDiffStoreCount();

        bool TryGetDiffStore(string npcId, out List<ContextDiff> diffs);
        bool TryGetKeyLastValues(string npcId, out Dictionary<string, string> values);
        void SetKeyLastValue(string npcId, string key, string value);
    }
}
