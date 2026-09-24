using System.Collections.Generic;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Context;
using RimMind.Application.Features.Context.Diff;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Features.Context
{
    public sealed class ContextDiffTracker : IContextDiffTracker
    {
        private readonly DiffComputer _computer = new();
        private readonly DiffRecorder _recorder;
        private readonly DiffMerger _merger = new();
        private readonly ILogSink? _log;

        public ContextDiffTracker(ILogSink? log = null)
        {
            _log = log;
            _recorder = new DiffRecorder();
        }

        public IReadOnlyDictionary<string, List<ContextDiff>> DiffStore => _recorder.DiffStore;
        public IReadOnlyDictionary<string, Dictionary<string, string>> KeyLastValues => _recorder.KeyLastValues;
        public IReadOnlyDictionary<string, Dictionary<string, float>> KeyLastNumericValues => _recorder.KeyLastNumericValues;

        public void AddDiff(string npcId, string key, string oldValue, string newValue, ContextLayer layer)
        {
            var diff = new ContextDiff { Key = key, OldValue = oldValue, NewValue = newValue, Layer = layer };
            _recorder.AddDiff(npcId, diff);
        }

        public void MergeExpiredDiffs(string npcId, IContextCacheManager cacheManager)
        {
            if (_recorder.TryGetDiffStore(npcId, out var diffs))
                _merger.MergeExpired(npcId, diffs, cacheManager);
        }

        public void UpdateKeyValues(string npcId, List<KeyMeta> keys, object? pawn,
            IContextCacheManager cacheManager, IBudgetScheduler scheduler)
        {
            if (keys == null || keys.Count == 0) return;

            // Get current values from L1 block cache
            var newValues = new Dictionary<string, string>();
            if (cacheManager.TryGetL1BlockCache(npcId, out var cachedBlocks))
            {
                foreach (var key in keys)
                {
                    if (cachedBlocks.TryGetValue(key.Key, out var value))
                        newValues[key.Key] = value;
                }
            }

            // Get old values from recorder
            if (!_recorder.TryGetKeyLastValues(npcId, out var oldValues))
                oldValues = new Dictionary<string, string>();

            // Compute diffs
            var layer = keys[0].Layer;
            var diffs = _computer.Compute(oldValues, newValues, layer);

            // Record diffs and update last values
            if (diffs.Count > 0)
            {
                _recorder.AddDiffs(npcId, diffs);
                _recorder.SetKeyLastValues(npcId, newValues);
            }
        }

        public void StoreNumericValues(string npcId, Dictionary<string, float> values)
            => _recorder.StoreNumericValues(npcId, values);

        public void ClearNpcDiffs(string npcId) => _recorder.ClearNpcDiffs(npcId);
        public void RemoveNpcKeyLastValues(string npcId) => _recorder.RemoveNpcKeyLastValues(npcId);
        public void Reset() => _recorder.Reset();
        public int GetDiffStoreCount() => _recorder.GetDiffStoreCount();
        public bool TryGetDiffStore(string npcId, out List<ContextDiff> diffs) => _recorder.TryGetDiffStore(npcId, out diffs);
        public bool TryGetKeyLastValues(string npcId, out Dictionary<string, string> values) => _recorder.TryGetKeyLastValues(npcId, out values);
        public void SetKeyLastValue(string npcId, string key, string value) => _recorder.SetKeyLastValue(npcId, key, value);
    }
}
