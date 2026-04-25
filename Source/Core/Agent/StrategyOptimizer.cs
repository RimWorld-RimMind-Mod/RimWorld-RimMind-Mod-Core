using System.Collections.Generic;
using RimMind.Core.Client;
using Verse;

namespace RimMind.Core.Agent
{
    public class StrategyOptimizer : IExposable
    {
        private Dictionary<string, float> _actionWeights = new Dictionary<string, float>();

        public void AdjustWeight(string action, float delta)
        {
            if (string.IsNullOrEmpty(action)) return;
            if (!_actionWeights.TryGetValue(action, out float current))
                current = 1.0f;
            _actionWeights[action] = UnityEngine.Mathf.Clamp(current + delta, 0f, 5f);
        }

        public void DecayAll()
        {
            var keys = new List<string>(_actionWeights.Keys);
            foreach (var key in keys)
            {
                if (_actionWeights.TryGetValue(key, out float current))
                {
                    _actionWeights[key] = UnityEngine.Mathf.Max(current * 0.999f, 0.5f);
                }
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
            Scribe_Collections.Look(ref _actionWeights, "actionWeights", LookMode.Value, LookMode.Value);
            _actionWeights ??= new Dictionary<string, float>();
        }
    }
}
