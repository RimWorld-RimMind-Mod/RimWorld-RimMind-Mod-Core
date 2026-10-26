using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Context;
using RimMind.Application.Common.Models;
using RimMind.Application.Common.Models.Context;
using RimMind.Domain.Interfaces;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Features.Context
{
    public sealed class BudgetScheduler : IBudgetScheduler
    {
        private const float DefaultTokensPerKey = 100f;
        private const int MaxTrackedCooldownEntries = 2048;

        private BudgetSchedulerConfig _config = new BudgetSchedulerConfig();
        private readonly IRelevanceTable? _relevanceTable;
        private readonly IRelevanceLearner? _learner;
        private readonly ITickProvider? _tickProvider;
        private readonly IEmbedCache? _embedCache;
        private readonly ConcurrentDictionary<(string Scenario, string NpcId, string Key), int> _lastIncludedTicks = new();

        public BudgetScheduler(IRelevanceTable? relevanceTable = null, IRelevanceLearner? learner = null, ITickProvider? tickProvider = null, IEmbedCache? embedCache = null)
        {
            _relevanceTable = relevanceTable;
            _learner = learner;
            _tickProvider = tickProvider;
            _embedCache = embedCache;
        }

        public BudgetSchedulerConfig Config => _config;

        public BudgetAllocation Schedule(List<KeyMeta> keys, string scenarioId, float budget, string? currentQuery)
        {
            int nowTicks = _tickProvider?.TicksGame ?? 0;
            var sctx = new ScoringContext
            {
                Scenario = scenarioId ?? string.Empty,
                NowTicks = nowTicks,
                Query = currentQuery,
                UserPinnedKeys = new HashSet<string>()
            };
            return ScheduleWithContext(keys, sctx, budget);
        }

        /// <summary>
        /// Schedule overload that accepts a pre-built ScoringContext for testing and advanced scenarios.
        /// Computes per-call scores into <see cref="BudgetAllocation.KeyScores"/> and tracks cooldown ticks
        /// per (scenario, npcId, key) without mutating shared <see cref="KeyMeta"/> instances.
        /// </summary>
        public BudgetAllocation ScheduleWithContext(List<KeyMeta> keys, ScoringContext sctx, float budget)
        {
            var result = new BudgetAllocation();
            if (keys == null || keys.Count == 0) return result;

            sctx ??= new ScoringContext();
            float totalWeight = 0;
            var scored = new List<(KeyMeta key, float score)>(keys.Count);
            foreach (var key in keys)
            {
                float score = ScoreKey(key, sctx);
                scored.Add((key, score));
                result.KeyScores[key.Key] = score;
                totalWeight += Math.Max(0, score);
            }

            if (totalWeight <= 0) return result;

            scored.Sort((a, b) => b.score.CompareTo(a.score));

            float tokenBudget = ResolveTokenBudget(scored, budget);
            float usedTokens = 0f;

            // First pass: always include essential keys (L0_Static and user-pinned keys)
            foreach (var (key, _) in scored)
            {
                if (!IsEssentialKey(key, sctx)) continue;
                AddToAllocation(result, key.Layer, key);
                usedTokens += EstimateKeyTokens(key);
                RecordIncludedTick(sctx, key.Key);
            }

            // Second pass: include highest-scoring optional keys within token budget
            foreach (var (key, _) in scored)
            {
                if (IsEssentialKey(key, sctx)) continue;
                float estimatedTokens = EstimateKeyTokens(key);
                if (tokenBudget < float.MaxValue && usedTokens + estimatedTokens > tokenBudget)
                {
                    continue;
                }

                usedTokens += estimatedTokens;
                AddToAllocation(result, key.Layer, key);
                RecordIncludedTick(sctx, key.Key);
            }

            return result;
        }

        public void OnKeyUpdated(KeyMeta key) { /* Intentionally empty: reserved for future adaptive budget rebalancing */ }
        public void Calibrate(List<KeyMeta> keys) { /* Intentionally empty: reserved for future auto-calibration */ }
        public void SetConfig(BudgetSchedulerConfig? config) { if (config != null) _config = config; }
        public BudgetSchedulerConfig GetConfig() => _config;

        /// <summary>
        /// 7-dimension scoring: Priority, SceneRelevance, QuerySimilarity, Recency, UseFeedback, UserPin, CooldownPenalty.
        /// </summary>
        internal float ScoreKey(KeyMeta key, ScoringContext sctx)
        {
            var w = _config;

            float P = key.GetEffectivePriority();
            float Rs = _relevanceTable?.GetRelevance(sctx.Scenario, key.Key) ?? 0.5f;
            float Q = ComputeQuerySimilarity(sctx.Query, key);
            float Rc = ComputeRecency(sctx.NowTicks, key);
            float F = GetFeedbackScore(sctx.Scenario, key);
            float Pin = sctx.UserPinnedKeys.Contains(key.Key) ? 1f : 0f;
            float Cd = ComputeCooldownPenalty(sctx, key);

            return w.W1 * P
                 + w.W2 * Rs
                 + w.W3 * Q
                 + w.W4 * Rc
                 + w.W5 * F
                 + 1000f * Pin    // hard boost for pinned keys
                 - w.W6 * Cd;
        }

        private float ComputeQuerySimilarity(string? query, KeyMeta key)
        {
            if (query is not { Length: > 0 } queryText || _embedCache == null) return 0f;
            if (key.LastValueEmbedding == null) return 0f;

            var queryEmbed = _embedCache.GetOrComputeQueryEmbedding(queryText);
            if (queryEmbed == null) return 0f;

            return CosineSimilarity(queryEmbed, key.LastValueEmbedding);
        }

        private float ComputeRecency(int nowTicks, KeyMeta key)
        {
            if (key.LastUpdatedTick <= 0) return 0.5f;  // unknown -> neutral
            int age = nowTicks - key.LastUpdatedTick;
            if (age < 0) return 1f;  // future tick -> treat as fresh
            return (float)Math.Exp(-age / (float)_config.RecencyHalflife);
        }

        private float ComputeCooldownPenalty(ScoringContext sctx, KeyMeta key)
        {
            int lastIncludedTick = GetLastIncludedTick(sctx, key);
            if (lastIncludedTick <= 0) return 0f;
            int delta = sctx.NowTicks - lastIncludedTick;
            if (delta < 0) return 0f;  // future tick -> no penalty
            if (delta >= _config.CooldownWindow) return 0f;
            return 1f - (float)delta / _config.CooldownWindow;
        }

        private int GetLastIncludedTick(ScoringContext sctx, KeyMeta key)
        {
            var scopeKey = (sctx.Scenario ?? string.Empty, sctx.NpcId ?? string.Empty, key.Key);
            if (_lastIncludedTicks.TryGetValue(scopeKey, out int tick))
                return tick;
            return key.LastIncludedTick;
        }

        private void RecordIncludedTick(ScoringContext sctx, string key)
        {
            if (sctx.NowTicks <= 0) return;
            if (_lastIncludedTicks.Count >= MaxTrackedCooldownEntries)
                _lastIncludedTicks.Clear();
            var scopeKey = (sctx.Scenario ?? string.Empty, sctx.NpcId ?? string.Empty, key);
            _lastIncludedTicks[scopeKey] = sctx.NowTicks;
        }

        private static bool IsEssentialKey(KeyMeta key, ScoringContext sctx)
        {
            if (key.Layer == ContextLayer.L0_Static) return true;
            if (sctx.UserPinnedKeys != null && sctx.UserPinnedKeys.Contains(key.Key)) return true;
            return false;
        }

        private static float EstimateKeyTokens(KeyMeta key)
        {
            return key.CurrentE > 0f ? key.CurrentE : DefaultTokensPerKey;
        }

        private static float ResolveTokenBudget(List<(KeyMeta key, float score)> scored, float budget)
        {
            if (budget <= 0f) return float.MaxValue;
            if (budget > 1.0f) return budget;

            bool hasExplicitEstimates = false;
            float totalEstimated = 0f;
            foreach (var (key, _) in scored)
            {
                if (key.CurrentE > 0f) hasExplicitEstimates = true;
                totalEstimated += EstimateKeyTokens(key);
            }

            if (hasExplicitEstimates)
            {
                return Math.Min(budget * RimMindDefaults.DefaultTotalBudget, totalEstimated * budget);
            }

            return budget * RimMindDefaults.DefaultTotalBudget;
        }

        private float GetFeedbackScore(string scenario, KeyMeta key)
        {
            return _learner?.GetFeedbackScore(scenario, key.Key) ?? 0.5f;
        }

        private static float CosineSimilarity(float[] a, float[] b)
        {
            if (a == null || b == null || a.Length != b.Length || a.Length == 0) return 0f;
            float dot = 0, normA = 0, normB = 0;
            for (int i = 0; i < a.Length; i++)
            {
                dot += a[i] * b[i];
                normA += a[i] * a[i];
                normB += b[i] * b[i];
            }
            var denom = (float)(Math.Sqrt(normA) * Math.Sqrt(normB));
            return denom > 0 ? dot / denom : 0f;
        }

        private static ContextLayer ChooseLayer(ContextLayer declared, float cumulative)
        {
            return declared;
        }

        private static void AddToAllocation(BudgetAllocation alloc, ContextLayer layer, KeyMeta key)
        {
            switch (layer)
            {
                case ContextLayer.L0_Static: alloc.L0Keys.Add(key); break;
                case ContextLayer.L1_Baseline: alloc.L1Keys.Add(key); break;
                case ContextLayer.L2_Environment: alloc.L2Keys.Add(key); break;
                case ContextLayer.L3_State: alloc.L3Keys.Add(key); break;
                case ContextLayer.L5_Sensor: alloc.L5Keys.Add(key); break;
            }
        }
    }
}
