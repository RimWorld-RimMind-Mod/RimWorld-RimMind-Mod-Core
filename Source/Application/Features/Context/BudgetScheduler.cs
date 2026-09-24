using System;
using System.Collections.Generic;
using System.Linq;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Context;
using RimMind.Application.Common.Models.Context;
using RimMind.Domain.Interfaces;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Features.Context
{
    public sealed class BudgetScheduler : IBudgetScheduler
    {
        private BudgetSchedulerConfig _config = new BudgetSchedulerConfig();
        private readonly IRelevanceTable? _relevanceTable;
        private readonly IRelevanceLearner? _learner;
        private readonly ITickProvider? _tickProvider;
        private readonly IEmbedCache? _embedCache;

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
            var result = new BudgetAllocation();
            if (keys == null || keys.Count == 0) return result;

            int nowTicks = _tickProvider?.TicksGame ?? 0;

            var sctx = new ScoringContext
            {
                Scenario = scenarioId,
                NowTicks = nowTicks,
                Query = currentQuery,
                UserPinnedKeys = new HashSet<string>()
            };

            float totalWeight = 0;
            var scored = new List<(KeyMeta key, float score)>();
            foreach (var key in keys)
            {
                float score = ScoreKey(key, sctx);
                scored.Add((key, score));
                totalWeight += Math.Max(0, score);
            }

            if (totalWeight <= 0) return result;

            scored.Sort((a, b) => b.score.CompareTo(a.score));

            float cumulative = 0;
            foreach (var (key, score) in scored)
            {
                float fraction = Math.Max(0, score) / totalWeight;
                cumulative += fraction;

                var target = ChooseLayer(key.Layer, cumulative);
                AddToAllocation(result, target, key);

                // Write back telemetry and cooldown tracking
                key.CurrentScore = score;
                key.LastIncludedTick = sctx.NowTicks;
            }

            return result;
        }

        /// <summary>
        /// Schedule overload that accepts a pre-built ScoringContext for testing and advanced scenarios.
        /// </summary>
        public BudgetAllocation ScheduleWithContext(List<KeyMeta> keys, ScoringContext sctx, float budget)
        {
            var result = new BudgetAllocation();
            if (keys == null || keys.Count == 0) return result;

            float totalWeight = 0;
            var scored = new List<(KeyMeta key, float score)>();
            foreach (var key in keys)
            {
                float score = ScoreKey(key, sctx);
                scored.Add((key, score));
                totalWeight += Math.Max(0, score);
            }

            if (totalWeight <= 0) return result;

            scored.Sort((a, b) => b.score.CompareTo(a.score));

            float cumulative = 0;
            foreach (var (key, score) in scored)
            {
                float fraction = Math.Max(0, score) / totalWeight;
                cumulative += fraction;

                var target = ChooseLayer(key.Layer, cumulative);
                AddToAllocation(result, target, key);

                key.CurrentScore = score;
                key.LastIncludedTick = sctx.NowTicks;
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
            float Cd = ComputeCooldownPenalty(sctx.NowTicks, key);

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

        private float ComputeCooldownPenalty(int nowTicks, KeyMeta key)
        {
            if (key.LastIncludedTick <= 0) return 0f;
            int delta = nowTicks - key.LastIncludedTick;
            if (delta < 0) return 0f;  // future tick -> no penalty
            if (delta >= _config.CooldownWindow) return 0f;
            return 1f - (float)delta / _config.CooldownWindow;
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
