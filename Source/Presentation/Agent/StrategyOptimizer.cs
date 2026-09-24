using System.Collections.Concurrent;
using System.Collections.Generic;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Domain.Llm;
using Verse;

namespace RimMind.Presentation.Agent
{
    public class StrategyOptimizer : IStrategyOptimizer, IExposable
    {
        private ConcurrentDictionary<string, float> _actionWeights = new ConcurrentDictionary<string, float>();

        public void AdjustWeight(string action, float delta)
        {
            if (string.IsNullOrEmpty(action)) return;
            _actionWeights.AddOrUpdate(action,
                UnityEngine.Mathf.Clamp(1.0f + delta, 0f, 5f),
                (_, current) => UnityEngine.Mathf.Clamp(current + delta, 0f, 5f));
        }

        public void DecayAll()
        {
            foreach (var key in _actionWeights.Keys)
            {
                _actionWeights.AddOrUpdate(key,
                    0.5f,
                    (_, current) => UnityEngine.Mathf.Max(current * 0.999f, 0.5f));
            }
        }

        public List<KeyValuePair<string, float>> GetTopN(int n)
        {
            var sorted = new List<KeyValuePair<string, float>>(_actionWeights);
            sorted.Sort((a, b) => b.Value.CompareTo(a.Value));
            return sorted.Count <= n ? sorted : sorted.GetRange(0, n);
        }

        public List<StructuredTool> GetWeightedTools(List<StructuredTool> tools)
        {
            if (tools == null || tools.Count <= 1)
                return tools!;

            var sorted = new List<StructuredTool>(tools);
            sorted.Sort((a, b) =>
            {
                float wa = _actionWeights.TryGetValue(a.Name, out var av) ? av : 1.0f;
                float wb = _actionWeights.TryGetValue(b.Name, out var bv) ? bv : 1.0f;
                return wb.CompareTo(wa);
            });
            return sorted;
        }

        public void ExposeData()
        {
            var dict = new Dictionary<string, float>(_actionWeights);
            Scribe_Collections.Look(ref dict, "actionWeights", LookMode.Value, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                _actionWeights = dict != null
                    ? new ConcurrentDictionary<string, float>(dict)
                    : new ConcurrentDictionary<string, float>();
            }
        }

    }
}
