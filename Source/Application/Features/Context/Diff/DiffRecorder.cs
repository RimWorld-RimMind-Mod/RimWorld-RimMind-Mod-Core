using System.Collections.Concurrent;
using System.Collections.Generic;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Features.Context.Diff
{
    /// <summary>
    /// Maintains active diff lists per NPC, with expire-tick tracking.
    /// </summary>
    internal sealed class DiffRecorder
    {
        private readonly ConcurrentDictionary<string, List<ContextDiff>> _diffStore = new();
        private readonly ConcurrentDictionary<string, Dictionary<string, string>> _keyLastValues = new();
        private readonly ConcurrentDictionary<string, Dictionary<string, float>> _keyLastNumericValues = new();

        public IReadOnlyDictionary<string, List<ContextDiff>> DiffStore => _diffStore;
        public IReadOnlyDictionary<string, Dictionary<string, string>> KeyLastValues => _keyLastValues;
        public IReadOnlyDictionary<string, Dictionary<string, float>> KeyLastNumericValues => _keyLastNumericValues;

        public void AddDiff(string npcId, ContextDiff diff)
        {
            var list = _diffStore.GetOrAdd(npcId, _ => new List<ContextDiff>());
            lock (list) { list.Add(diff); }
        }

        public void AddDiffs(string npcId, IEnumerable<ContextDiff> diffs)
        {
            var list = _diffStore.GetOrAdd(npcId, _ => new List<ContextDiff>());
            lock (list) { list.AddRange(diffs); }
        }

        public void StoreNumericValues(string npcId, Dictionary<string, float> values)
        {
            _keyLastNumericValues[npcId] = new Dictionary<string, float>(values);
        }

        public void SetKeyLastValue(string npcId, string key, string value)
        {
            var dict = _keyLastValues.GetOrAdd(npcId, _ => new Dictionary<string, string>());
            lock (dict) { dict[key] = value; }
        }

        public void SetKeyLastValues(string npcId, Dictionary<string, string> values)
        {
            _keyLastValues[npcId] = new Dictionary<string, string>(values);
        }

        public bool TryGetDiffStore(string npcId, out List<ContextDiff> diffs)
        {
            return _diffStore.TryGetValue(npcId, out diffs!);
        }

        public bool TryGetKeyLastValues(string npcId, out Dictionary<string, string> values)
        {
            return _keyLastValues.TryGetValue(npcId, out values!);
        }

        public void ClearNpcDiffs(string npcId) => _diffStore.TryRemove(npcId, out _);
        public void RemoveNpcKeyLastValues(string npcId) => _keyLastValues.TryRemove(npcId, out _);
        public void Reset() { _diffStore.Clear(); _keyLastValues.Clear(); _keyLastNumericValues.Clear(); }
        public int GetDiffStoreCount() => _diffStore.Count;
    }
}
