using System;
using System.Collections.Generic;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Flywheel;
using RimMind.Application.Common.Models.Flywheel;
using RimMind.Application.Common.Models;

namespace RimMind.Application.Features.Flywheel
{
    internal sealed class FlywheelRuleEngine : IFlywheelRuleEngine
    {
        private readonly IFlywheelParameterStore _store;
        private readonly ILogSink? _log;

        public FlywheelRuleEngine(IFlywheelParameterStore store, ILogSink? log = null)
        {
            _store = store;
            _log = log;
        }

        public void Analyze(List<TelemetryRecord> records)
        {
            if (records == null || records.Count == 0) return;

            _log?.Message($"Analyzing {records.Count} telemetry records");

            float totalTokens = 0f;
            int tokenSampleCount = 0;
            foreach (var record in records)
            {
                if (record == null) continue;
                if (string.Equals(record.Metric, "unified_tokens_used", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(record.Metric, "tokens_used", StringComparison.OrdinalIgnoreCase))
                {
                    totalTokens += record.Value;
                    tokenSampleCount++;
                }
            }

            if (tokenSampleCount == 0) return;

            float budget = _store.Get("ContextBudget");
            if (budget <= 0f) return;

            float avgTokens = totalTokens / tokenSampleCount;
            float ratio = avgTokens / (budget * RimMindDefaults.MaxTokens);

            if (ratio > 0.9f)
            {
                _log?.Warning("Token usage ratio exceeds 90%, consider increasing ContextBudget");
            }
            else if (ratio < 0.3f)
            {
                _log?.Message("Token usage ratio below 30%, consider decreasing ContextBudget");
            }
        }

        public List<ParameterRecommendation> Evaluate(Dictionary<string, float> metrics)
        {
            var recommendations = new List<ParameterRecommendation>();
            if (metrics == null) return recommendations;

            bool hasTokens = metrics.TryGetValue("unified_tokens_used_avg", out var avgTokens)
                || metrics.TryGetValue("avg_tokens_used", out avgTokens);
            if (hasTokens && avgTokens > 0f)
            {
                float budget = _store.Get("ContextBudget");
                if (budget <= 0f) return recommendations;

                float ratio = avgTokens / (budget * RimMindDefaults.MaxTokens);
                if (ratio > 0.9f)
                {
                    recommendations.Add(new ParameterRecommendation
                    {
                        Target = "ContextBudget",
                        CurrentValue = budget,
                        RecommendedValue = Math.Min(2.0f, budget * 1.1f),
                        Confidence = 0.8f,
                        Reason = "Token usage ratio > 90%, increasing budget"
                    });
                }
                else if (ratio < 0.3f)
                {
                    recommendations.Add(new ParameterRecommendation
                    {
                        Target = "ContextBudget",
                        CurrentValue = budget,
                        RecommendedValue = Math.Max(0.3f, budget * 0.9f),
                        Confidence = 0.6f,
                        Reason = "Token usage ratio < 30%, decreasing budget"
                    });
                }
            }

            return recommendations;
        }
    }
}
