using System.Collections.Generic;

namespace RimMind.Core.Context
{
    public class EmbedCache
    {
        private const int MaxBlockEntries = 200;
        private const int MaxEntryItems = 500;

        private readonly Dictionary<string, Dictionary<string, float[]>> _blockCache = new Dictionary<string, Dictionary<string, float[]>>();
        private readonly Dictionary<string, Dictionary<string, Dictionary<int, float[]>>> _entryCache = new Dictionary<string, Dictionary<string, Dictionary<int, float[]>>>();
        private readonly Dictionary<string, ContextLayer> _blockLayer = new Dictionary<string, ContextLayer>();
        private readonly LinkedList<string> _blockOrder = new LinkedList<string>();
        private readonly LinkedList<string> _entryOrder = new LinkedList<string>();
        private int _blockCount = 0;
        private int _entryCount = 0;

        public void SetBlockEmbedding(string npcId, string key, float[] embedding, ContextLayer layer = ContextLayer.L3_State)
        {
            if (!_blockCache.TryGetValue(npcId, out var dict))
            {
                dict = new Dictionary<string, float[]>();
                _blockCache[npcId] = dict;
                _blockOrder.AddLast(npcId);
            }
            if (!dict.ContainsKey(key)) _blockCount++;
            dict[key] = embedding;
            _blockLayer[npcId + ":" + key] = layer;
            EvictBlockIfNeeded();
        }

        public float[]? GetBlockEmbedding(string npcId, string key)
        {
            if (_blockCache.TryGetValue(npcId, out var dict) && dict.TryGetValue(key, out var emb))
                return emb;
            return null;
        }

        public void SetEntryEmbedding(string npcId, string key, int entryIndex, float[] embedding)
        {
            if (!_entryCache.TryGetValue(npcId, out var keyDict))
            {
                keyDict = new Dictionary<string, Dictionary<int, float[]>>();
                _entryCache[npcId] = keyDict;
                _entryOrder.AddLast(npcId);
            }
            if (!keyDict.TryGetValue(key, out var idxDict))
            {
                idxDict = new Dictionary<int, float[]>();
                keyDict[key] = idxDict;
            }
            if (!idxDict.ContainsKey(entryIndex)) _entryCount++;
            idxDict[entryIndex] = embedding;
            EvictEntryIfNeeded();
        }

        public float[]? GetEntryEmbedding(string npcId, string key, int entryIndex)
        {
            if (_entryCache.TryGetValue(npcId, out var keyDict) &&
                keyDict.TryGetValue(key, out var idxDict) &&
                idxDict.TryGetValue(entryIndex, out var emb))
                return emb;
            return null;
        }

        public void InvalidateBlock(string npcId, string key)
        {
            if (_blockCache.TryGetValue(npcId, out var dict))
            {
                if (dict.Remove(key)) _blockCount--;
            }
            _blockLayer.Remove(npcId + ":" + key);
        }

        public void InvalidateEntries(string npcId, string key)
        {
            if (_entryCache.TryGetValue(npcId, out var keyDict))
            {
                if (keyDict.TryGetValue(key, out var idxDict))
                    _entryCount -= idxDict.Count;
                keyDict.Remove(key);
            }
        }

        public void InvalidateNpc(string npcId)
        {
            if (_blockCache.TryGetValue(npcId, out var dict))
            {
                _blockCount -= dict.Count;
                _blockCache.Remove(npcId);
                _blockOrder.Remove(npcId);
            }
            if (_entryCache.TryGetValue(npcId, out var keyDict))
            {
                foreach (var idxDict in keyDict.Values)
                    _entryCount -= idxDict.Count;
                _entryCache.Remove(npcId);
                _entryOrder.Remove(npcId);
            }
            var keysToRemove = new List<string>();
            foreach (var k in _blockLayer.Keys)
            {
                if (k.StartsWith(npcId + ":"))
                    keysToRemove.Add(k);
            }
            foreach (var k in keysToRemove)
                _blockLayer.Remove(k);
        }

        public void Clear()
        {
            _blockCache.Clear();
            _entryCache.Clear();
            _blockOrder.Clear();
            _entryOrder.Clear();
            _blockLayer.Clear();
            _blockCount = 0;
            _entryCount = 0;
        }

        private void EvictBlockIfNeeded()
        {
            while (_blockCount > MaxBlockEntries && _blockOrder.Count > 0)
            {
                string? victim = null;
                var node = _blockOrder.First;
                while (node != null)
                {
                    bool hasProtected = false;
                    if (_blockCache.TryGetValue(node.Value, out var dict))
                    {
                        foreach (var k in dict.Keys)
                        {
                            if (_blockLayer.TryGetValue(node.Value + ":" + k, out var layer) &&
                                (layer == ContextLayer.L0_Static || layer == ContextLayer.L1_Baseline))
                            {
                                hasProtected = true;
                                break;
                            }
                        }
                    }
                    if (!hasProtected)
                    {
                        victim = node.Value;
                        break;
                    }
                    node = node.Next;
                }
                if (victim == null) break;
                _blockOrder.Remove(victim);
                if (_blockCache.TryGetValue(victim, out var victimDict))
                {
                    _blockCount -= victimDict.Count;
                    _blockCache.Remove(victim);
                }
            }
        }

        private void EvictEntryIfNeeded()
        {
            while (_entryCount > MaxEntryItems && _entryOrder.Count > 0)
            {
                var oldest = _entryOrder.First.Value;
                _entryOrder.RemoveFirst();
                if (_entryCache.TryGetValue(oldest, out var keyDict))
                {
                    foreach (var idxDict in keyDict.Values)
                        _entryCount -= idxDict.Count;
                    _entryCache.Remove(oldest);
                }
            }
        }
    }
}
