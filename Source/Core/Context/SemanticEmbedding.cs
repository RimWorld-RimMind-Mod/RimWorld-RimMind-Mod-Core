using System;
using System.Collections.Generic;
using Verse;

namespace RimMind.Core.Context
{
    public interface IRelevanceProvider
    {
        float ComputeRelevance(string scenarioId, string npcId, KeyMeta key);
    }

    public static class SemanticEmbedding
    {
        private static readonly Dictionary<string, float[]> _keyEmbeddings = new Dictionary<string, float[]>();
        private static readonly Dictionary<string, float[]> _scenarioEmbeddings = new Dictionary<string, float[]>();
        private static readonly Dictionary<string, Dictionary<string, float[]>> _blockEmbeddings = new Dictionary<string, Dictionary<string, float[]>>();
        private static readonly Dictionary<string, Dictionary<string, Dictionary<int, float[]>>> _entryEmbeddings = new Dictionary<string, Dictionary<string, Dictionary<int, float[]>>>();
        private static bool _initialized = false;
        private static int _dimension = 0;

        public static int Dimension => _dimension;

        public static void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

            RegisterKeyEmbedding("health", new float[] { 0.9f, 0.1f, 0.8f, 0.3f, 0.1f, 0.2f, 0.1f, 0.3f });
            RegisterKeyEmbedding("mood", new float[] { 0.5f, 0.8f, 0.3f, 0.7f, 0.2f, 0.1f, 0.3f, 0.2f });
            RegisterKeyEmbedding("current_job", new float[] { 0.4f, 0.3f, 0.2f, 0.8f, 0.3f, 0.5f, 0.2f, 0.4f });
            RegisterKeyEmbedding("combat_status", new float[] { 0.95f, 0.1f, 0.9f, 0.1f, 0.1f, 0.3f, 0.2f, 0.5f });
            RegisterKeyEmbedding("target_info", new float[] { 0.8f, 0.2f, 0.7f, 0.1f, 0.1f, 0.4f, 0.2f, 0.4f });
            RegisterKeyEmbedding("task_progress", new float[] { 0.3f, 0.5f, 0.2f, 0.9f, 0.4f, 0.6f, 0.3f, 0.3f });
            RegisterKeyEmbedding("nearby_pawns", new float[] { 0.3f, 0.9f, 0.2f, 0.4f, 0.5f, 0.2f, 0.4f, 0.2f });
            RegisterKeyEmbedding("colony_status", new float[] { 0.5f, 0.4f, 0.3f, 0.6f, 0.8f, 0.7f, 0.3f, 0.5f });
            RegisterKeyEmbedding("current_area", new float[] { 0.3f, 0.3f, 0.2f, 0.5f, 0.6f, 0.4f, 0.2f, 0.3f });
            RegisterKeyEmbedding("weather", new float[] { 0.2f, 0.1f, 0.1f, 0.3f, 0.7f, 0.3f, 0.1f, 0.6f });
            RegisterKeyEmbedding("time_of_day", new float[] { 0.2f, 0.2f, 0.1f, 0.4f, 0.5f, 0.3f, 0.1f, 0.4f });
            RegisterKeyEmbedding("season", new float[] { 0.1f, 0.1f, 0.1f, 0.2f, 0.6f, 0.2f, 0.1f, 0.5f });
            RegisterKeyEmbedding("map_structure", new float[] { 0.3f, 0.2f, 0.2f, 0.5f, 0.8f, 0.5f, 0.2f, 0.4f });
            RegisterKeyEmbedding("pawn_base_info", new float[] { 0.4f, 0.6f, 0.3f, 0.5f, 0.3f, 0.2f, 0.5f, 0.2f });
            RegisterKeyEmbedding("fixed_relations", new float[] { 0.2f, 0.9f, 0.1f, 0.3f, 0.3f, 0.1f, 0.6f, 0.1f });
            RegisterKeyEmbedding("ideology", new float[] { 0.2f, 0.7f, 0.1f, 0.3f, 0.2f, 0.1f, 0.8f, 0.1f });
            RegisterKeyEmbedding("skills_summary", new float[] { 0.3f, 0.4f, 0.2f, 0.8f, 0.3f, 0.5f, 0.3f, 0.3f });
            RegisterKeyEmbedding("memory_pawn", new float[] { 0.4f, 0.6f, 0.3f, 0.5f, 0.3f, 0.2f, 0.5f, 0.3f });
            RegisterKeyEmbedding("working_memory", new float[] { 0.5f, 0.5f, 0.4f, 0.7f, 0.3f, 0.4f, 0.4f, 0.3f });
            RegisterKeyEmbedding("memory_narrator", new float[] { 0.2f, 0.3f, 0.1f, 0.4f, 0.7f, 0.8f, 0.2f, 0.6f });

            RegisterScenarioEmbedding(ScenarioIds.Decision, new float[] { 0.9f, 0.3f, 0.8f, 0.7f, 0.3f, 0.4f, 0.2f, 0.4f });
            RegisterScenarioEmbedding(ScenarioIds.Dialogue, new float[] { 0.4f, 0.9f, 0.2f, 0.5f, 0.3f, 0.2f, 0.6f, 0.2f });
            RegisterScenarioEmbedding(ScenarioIds.Personality, new float[] { 0.3f, 0.8f, 0.2f, 0.4f, 0.2f, 0.2f, 0.9f, 0.2f });
            RegisterScenarioEmbedding(ScenarioIds.Storyteller, new float[] { 0.5f, 0.3f, 0.4f, 0.5f, 0.9f, 0.8f, 0.2f, 0.7f });
        }

        public static void RegisterKeyEmbedding(string key, float[] embedding)
        {
            EnsureInitialized();
            if (embedding == null || embedding.Length == 0) return;
            ValidateDimension(embedding.Length);
            _keyEmbeddings[key] = Normalize(embedding);
        }

        public static void RegisterScenarioEmbedding(string scenarioId, float[] embedding)
        {
            EnsureInitialized();
            if (embedding == null || embedding.Length == 0) return;
            ValidateDimension(embedding.Length);
            _scenarioEmbeddings[scenarioId] = Normalize(embedding);
        }

        public static void SetBlockEmbedding(string npcId, string key, float[] embedding)
        {
            if (embedding == null || embedding.Length == 0) return;
            ValidateDimension(embedding.Length);
            if (!_blockEmbeddings.TryGetValue(npcId, out var dict))
            {
                dict = new Dictionary<string, float[]>();
                _blockEmbeddings[npcId] = dict;
            }
            dict[key] = Normalize(embedding);
        }

        public static float[]? GetBlockEmbedding(string npcId, string key)
        {
            if (_blockEmbeddings.TryGetValue(npcId, out var dict) && dict.TryGetValue(key, out var emb))
                return emb;
            return null;
        }

        public static void SetEntryEmbedding(string npcId, string key, int entryIndex, float[] embedding)
        {
            if (embedding == null || embedding.Length == 0) return;
            ValidateDimension(embedding.Length);
            if (!_entryEmbeddings.TryGetValue(npcId, out var keyDict))
            {
                keyDict = new Dictionary<string, Dictionary<int, float[]>>();
                _entryEmbeddings[npcId] = keyDict;
            }
            if (!keyDict.TryGetValue(key, out var idxDict))
            {
                idxDict = new Dictionary<int, float[]>();
                keyDict[key] = idxDict;
            }
            idxDict[entryIndex] = Normalize(embedding);
        }

        public static float[]? GetEntryEmbedding(string npcId, string key, int entryIndex)
        {
            if (_entryEmbeddings.TryGetValue(npcId, out var keyDict) &&
                keyDict.TryGetValue(key, out var idxDict) &&
                idxDict.TryGetValue(entryIndex, out var emb))
                return emb;
            return null;
        }

        public static void InvalidateBlockEmbedding(string npcId, string key)
        {
            if (_blockEmbeddings.TryGetValue(npcId, out var dict))
                dict.Remove(key);
        }

        public static void InvalidateEntryEmbeddings(string npcId, string key)
        {
            if (_entryEmbeddings.TryGetValue(npcId, out var keyDict))
                keyDict.Remove(key);
        }

        public static void InvalidateNpc(string npcId)
        {
            _blockEmbeddings.Remove(npcId);
            _entryEmbeddings.Remove(npcId);
        }

        public static float ComputeRelevance(string scenarioId, string npcId, KeyMeta key)
        {
            EnsureInitialized();
            if (!_scenarioEmbeddings.TryGetValue(scenarioId, out var sVec)) return 0.5f;

            if (key.IsIndexable)
            {
                float maxSim = 0f;
                bool found = false;
                if (_entryEmbeddings.TryGetValue(npcId, out var keyDict) &&
                    keyDict.TryGetValue(key.Key, out var idxDict))
                {
                    foreach (var kvp in idxDict)
                    {
                        float sim = CosineSimilarity(sVec, kvp.Value);
                        if (sim > maxSim) maxSim = sim;
                        found = true;
                    }
                }
                if (found) return maxSim;
            }

            if (_blockEmbeddings.TryGetValue(npcId, out var blockDict) &&
                blockDict.TryGetValue(key.Key, out var blockEmb))
            {
                return CosineSimilarity(sVec, blockEmb);
            }

            if (key.KeyEmbedding != null)
            {
                return CosineSimilarity(sVec, key.KeyEmbedding);
            }

            if (_keyEmbeddings.TryGetValue(key.Key, out var keyEmb))
            {
                return CosineSimilarity(sVec, keyEmb);
            }

            return 0.5f;
        }

        public static float ComputeKeySimilarity(string scenarioId, string key)
        {
            EnsureInitialized();
            if (!_scenarioEmbeddings.TryGetValue(scenarioId, out var sVec)) return 0.5f;
            if (!_keyEmbeddings.TryGetValue(key, out var kVec)) return 0.5f;
            return CosineSimilarity(sVec, kVec);
        }

        private static void ValidateDimension(int dim)
        {
            if (_dimension == 0)
            {
                _dimension = dim;
                return;
            }
            if (_dimension != dim)
                throw new ArgumentException($"[RimMind] Embedding dimension mismatch: expected {_dimension}, got {dim}");
        }

        private static float CosineSimilarity(float[] a, float[] b)
        {
            if (a.Length != b.Length)
                throw new ArgumentException($"[RimMind] CosineSimilarity dimension mismatch: {a.Length} vs {b.Length}");
            float dot = 0, normA = 0, normB = 0;
            for (int i = 0; i < a.Length; i++)
            {
                dot += a[i] * b[i];
                normA += a[i] * a[i];
                normB += b[i] * b[i];
            }
            float denom = (float)Math.Sqrt(normA * normB);
            if (denom < 1e-8f) return 0f;
            return Math.Clamp(dot / denom, 0f, 1f);
        }

        private static float[] Normalize(float[] v)
        {
            float norm = 0;
            foreach (var x in v) norm += x * x;
            norm = (float)Math.Sqrt(norm);
            if (norm < 1e-8f) return v;
            var result = new float[v.Length];
            for (int i = 0; i < v.Length; i++)
                result[i] = v[i] / norm;
            return result;
        }
    }

    public class EmbeddingRelevanceProvider : IRelevanceProvider
    {
        public float ComputeRelevance(string scenarioId, string npcId, KeyMeta key)
        {
            return SemanticEmbedding.ComputeRelevance(scenarioId, npcId, key);
        }
    }
}
