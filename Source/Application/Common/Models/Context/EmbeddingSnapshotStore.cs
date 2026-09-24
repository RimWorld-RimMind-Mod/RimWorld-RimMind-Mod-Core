using System.Collections.Concurrent;
using System.Collections.Generic;

namespace RimMind.Application.Common.Models.Context
{
    public class EmbeddingSnapshotRecord
    {
        public string NpcId = "";
        public string ScenarioId = "";
        public string Key = "";
        public string Layer = "";
        public string SourceText = "";
        public float[]? Vector;
        public float RelevanceScore;
        public long TimestampTicks;
    }

    public class EmbeddingSnapshotStore
    {
        private readonly ConcurrentDictionary<string, List<EmbeddingSnapshotRecord>> _store
            = new ConcurrentDictionary<string, List<EmbeddingSnapshotRecord>>();

        private const int MaxRecordsPerNpc = RimMindDefaults.EmbedMaxRecordsPerNpc;

        public void Record(EmbeddingSnapshotRecord record)
        {
            if (record == null || string.IsNullOrEmpty(record.NpcId)) return;

            var list = _store.GetOrAdd(record.NpcId, _ => new List<EmbeddingSnapshotRecord>());
            lock (list)
            {
                list.Add(record);
                if (list.Count > MaxRecordsPerNpc)
                    list.RemoveRange(0, list.Count - MaxRecordsPerNpc);
            }
        }

        public bool TryGetRecords(string npcId, out List<EmbeddingSnapshotRecord>? records)
        {
            return _store.TryGetValue(npcId, out records);
        }

        public void Remove(string npcId)
        {
            _store.TryRemove(npcId, out _);
        }

        public void Clear()
        {
            _store.Clear();
        }

        public int GetRecordCount(string npcId)
        {
            if (_store.TryGetValue(npcId, out var list))
                return list.Count;
            return 0;
        }
    }
}
