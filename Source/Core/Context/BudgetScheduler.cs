using System;
using System.Collections.Generic;
using System.Linq;
using RimMind.Core.Extensions;
using RimMind.Core.Flywheel;
using Verse;

namespace RimMind.Core.Context
{
    public class BudgetScheduler
    {
        private BudgetSchedulerConfig _config;
        private IRelevanceProvider? _relevanceProvider;

        public BudgetScheduler() { _config = new BudgetSchedulerConfig(); }

        public BudgetScheduler(IRelevanceProvider relevanceProvider)
        {
            _config = new BudgetSchedulerConfig();
            _relevanceProvider = relevanceProvider;
        }

        public BudgetScheduler(BudgetSchedulerConfig config, IRelevanceProvider? relevanceProvider = null)
        {
            _config = config ?? new BudgetSchedulerConfig();
            _relevanceProvider = relevanceProvider;
        }

        public BudgetSchedulerConfig GetConfig() => _config;

        public void SubscribeParameterStore()
        {
            var store = FlywheelParameterStore.Instance;
            if (store == null) return;
            ApplyStoreParameters(store);
            RimMindAPI.RegisterParameterTuner(new BudgetSchedulerTuner(this));
        }

        private void OnParameterChanged(string key, float value)
        {
            switch (key)
            {
                case "w1":
                    _config.W1 = value;
                    _config.W2 = 1f - value;
                    break;
                case "w2":
                    _config.W2 = value;
                    _config.W1 = 1f - value;
                    break;
                case "Alpha":
                    _config.Alpha = value;
                    break;
                case "AlphaSmooth":
                    _config.AlphaSmooth = value;
                    break;
                case "PromoteThreshold":
                    _config.PromoteThreshold = value;
                    break;
                case "DemoteThreshold":
                    _config.DemoteThreshold = value;
                    break;
            }
        }

        private void ApplyStoreParameters(FlywheelParameterStore store)
        {
            _config.W1 = store.Get("w1");
            _config.W2 = store.Get("w2");
            _config.Alpha = store.Get("Alpha");
            _config.AlphaSmooth = store.Get("AlphaSmooth");
            _config.PromoteThreshold = store.Get("PromoteThreshold");
            _config.DemoteThreshold = store.Get("DemoteThreshold");
        }

        public void SetRelevanceProvider(IRelevanceProvider provider)
        {
            _relevanceProvider = provider;
        }

        public void SetConfig(BudgetSchedulerConfig config)
        {
            _config = config ?? new BudgetSchedulerConfig();
        }

        public BudgetScheduleResult Schedule(
            List<KeyMeta> keys,
            string scenarioId,
            float budget,
            string? currentQuery)
        {
            float B = Math.Clamp(budget, 0f, 1f);
            var result = new BudgetScheduleResult();

            result.L0Keys = keys.Where(k => k.Layer == ContextLayer.L0_Static).ToList();

            if (B >= 0.2f)
                result.L1Keys = keys.Where(k => k.Layer == ContextLayer.L1_Baseline).ToList();

            float threshold = 1.0f - B;
            var l2l3Keys = keys.Where(k => k.Layer == ContextLayer.L2_Environment || k.Layer == ContextLayer.L3_State);
            foreach (var key in l2l3Keys)
            {
                float P = key.GetEffectivePriority();
                float E = ComputeRelevance(scenarioId, "", key);
                var coreSettings = RimMind.Core.RimMindCoreMod.Settings?.Context;
                float w1 = coreSettings?.BudgetW1 ?? _config.W1;
                float w2 = coreSettings?.BudgetW2 ?? _config.W2;
                float score = w1 * P + w2 * E;
                key.CurrentScore = score;
                key.CurrentE = E;

                if (score >= threshold)
                {
                    if (key.Layer == ContextLayer.L2_Environment)
                        result.L2Keys.Add(key);
                    else
                        result.L3Keys.Add(key);
                }
            }

            if (B >= 0.3f)
            {
                var l5Keys = keys.Where(k => k.Layer == ContextLayer.L5_Sensor);
                foreach (var key in l5Keys)
                {
                    float P = key.GetEffectivePriority();
                    float E = ComputeRelevance(scenarioId, "", key);
                    var coreSettings = RimMind.Core.RimMindCoreMod.Settings?.Context;
                    float w1 = coreSettings?.BudgetW1 ?? _config.W1;
                    float w2 = coreSettings?.BudgetW2 ?? _config.W2;
                    float score = w1 * P + w2 * E;
                    key.CurrentScore = score;
                    key.CurrentE = E;

                    if (score >= threshold)
                        result.L5Keys.Add(key);
                }
            }

            if (B < 0.1f && result.L3Keys.Count > 1)
            {
                result.L3Keys = result.L3Keys.OrderByDescending(k => k.CurrentScore).Take(1).ToList();
            }

            int baseRounds = ScenarioRegistry.GetBaseRounds(scenarioId);
            result.MaxHistoryRounds = Math.Max(1, (int)Math.Ceiling(B * baseRounds));
            result.MaxRagResults = (int)Math.Ceiling(B * 5);

            result.UseFullValue = B > 0.7f;
            result.UseDiff = B > 0.2f && B <= 0.7f;

            return result;
        }

        public void OnKeyUpdated(KeyMeta key)
        {
            key.UpdateCount++;
            float pReal = 1f / (1f + _config.Alpha * key.UpdateCount);
            key.AdaptivePriority = _config.AlphaSmooth * key.AdaptivePriority + (1f - _config.AlphaSmooth) * pReal;
        }

        public void Calibrate(List<KeyMeta> keys)
        {
            foreach (var key in keys)
            {
                key.UpdateCount = Math.Max(0, key.UpdateCount / 2);
                float pReal = 1f / (1f + _config.Alpha * key.UpdateCount);
                key.AdaptivePriority = _config.AlphaSmooth * key.AdaptivePriority + (1f - _config.AlphaSmooth) * pReal;
            }

            foreach (var key in keys)
            {
                if (key.OriginalLayer == ContextLayer.L0_Static) continue;
                var newLayer = ComputeEffectiveLayer(key);
                if (newLayer != key.Layer)
                {
                    Log.Message($"[RimMind] Key '{key.Key}' layer changed: {key.Layer} → {newLayer}");
                    key.Layer = newLayer;
                }
            }
        }

        private ContextLayer ComputeEffectiveLayer(KeyMeta key)
        {
            float effectiveP = key.GetEffectivePriority();
            ContextLayer current = key.Layer;

            if (effectiveP >= _config.PromoteThreshold && key.UpdateCount == 0)
            {
                if (current == ContextLayer.L3_State) return ContextLayer.L2_Environment;
                if (current == ContextLayer.L2_Environment) return ContextLayer.L1_Baseline;
            }

            if (effectiveP <= _config.DemoteThreshold && key.UpdateCount > 5)
            {
                if (current == ContextLayer.L1_Baseline) return ContextLayer.L2_Environment;
                if (current == ContextLayer.L2_Environment) return ContextLayer.L3_State;
            }

            return current;
        }

        private float ComputeRelevance(string scenarioId, string npcId, KeyMeta key)
        {
            float tableValue = RelevanceTable.GetRelevance(scenarioId, key.Key);
            if (_relevanceProvider != null)
            {
                float embeddingValue = _relevanceProvider.ComputeRelevance(scenarioId, npcId, key);
                return 0.6f * tableValue + 0.4f * embeddingValue;
            }
            return tableValue;
        }
    }

    public class BudgetSchedulerTuner : IParameterTuner
    {
        private readonly BudgetScheduler _scheduler;

        public string TunerId => "BudgetScheduler";

        public BudgetSchedulerTuner(BudgetScheduler scheduler)
        {
            _scheduler = scheduler;
        }

        public void Tune(BudgetSchedulerConfig config)
        {
            _scheduler.SetConfig(config);
        }
    }
}
