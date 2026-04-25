using System;
using System.Collections.Generic;
using System.Linq;
using RimMind.Core.Settings;
using Verse;

namespace RimMind.Core.Flywheel
{
    public class ParameterRecommendation
    {
        public string Target = null!;
        public float CurrentValue;
        public float RecommendedValue;
        public float Confidence;
        public string Reason = null!;
    }

    public static class FlywheelRuleEngine
    {
        public static List<ParameterRecommendation> Analyze(List<TelemetryRecord> records)
        {
            var recommendations = new List<ParameterRecommendation>();
            if (records == null || records.Count == 0) return recommendations;

            var store = FlywheelParameterStore.Instance;

            float avgBudgetUtil = ComputeAvgBudgetUtilization(records);
            float avgCacheHitRate = ComputeAvgCacheHitRate(records);
            float avgParseSuccessRate = ComputeAvgParseSuccessRate(records);
            float avgTrimRatio = ComputeAvgTrimRatio(records);

            var metrics = new Dictionary<string, float>
            {
                { "AvgBudgetUtilization", avgBudgetUtil },
                { "AvgCacheHitRate", avgCacheHitRate },
                { "AvgParseSuccessRate", avgParseSuccessRate },
                { "AvgTrimRatio", avgTrimRatio },
            };

            if (avgBudgetUtil < 0.5f && store != null)
            {
                float currentBudget = store.Get("TotalBudget");
                recommendations.Add(new AnalysisRecommendationEntry
                {
                    Target = "TotalBudget",
                    CurrentValue = currentBudget,
                    RecommendedValue = currentBudget * 0.8f,
                    Confidence = 0.7f,
                    Reason = $"Budget utilization only {avgBudgetUtil:P0}, consider reducing TotalBudget",
                    TriggerRule = "BudgetUtilization < 0.5",
                    ComputationDetail = $"Current={currentBudget}, Recommended={currentBudget}*0.8={currentBudget * 0.8f}",
                });
            }

            if (avgBudgetUtil > 0.95f && store != null)
            {
                float currentBudget = store.Get("TotalBudget");
                recommendations.Add(new AnalysisRecommendationEntry
                {
                    Target = "TotalBudget",
                    CurrentValue = currentBudget,
                    RecommendedValue = currentBudget * 1.2f,
                    Confidence = 0.8f,
                    Reason = $"Budget utilization {avgBudgetUtil:P0}, risk of over-trimming",
                    TriggerRule = "BudgetUtilization > 0.95",
                    ComputationDetail = $"Current={currentBudget}, Recommended={currentBudget}*1.2={currentBudget * 1.2f}",
                });
            }

            if (avgCacheHitRate < 0.3f && store != null)
            {
                float currentAlpha = store.Get("Alpha");
                recommendations.Add(new AnalysisRecommendationEntry
                {
                    Target = "Alpha",
                    CurrentValue = currentAlpha,
                    RecommendedValue = currentAlpha * 0.5f,
                    Confidence = 0.6f,
                    Reason = $"Cache hit rate only {avgCacheHitRate:P0}, reduce Alpha to slow priority decay",
                    TriggerRule = "CacheHitRate < 0.3",
                    ComputationDetail = $"Current={currentAlpha}, Recommended={currentAlpha}*0.5={currentAlpha * 0.5f}",
                });
            }

            if (avgTrimRatio > 0.4f && store != null)
            {
                float currentW1 = store.Get("w1");
                recommendations.Add(new AnalysisRecommendationEntry
                {
                    Target = "w1",
                    CurrentValue = currentW1,
                    RecommendedValue = Math.Min(0.7f, currentW1 + 0.1f),
                    Confidence = 0.65f,
                    Reason = $"Trim ratio {avgTrimRatio:P0} too high, increase w1 to prioritize stability",
                    TriggerRule = "TrimRatio > 0.4",
                    ComputationDetail = $"Current={currentW1}, Recommended=min(0.7, {currentW1}+0.1)={Math.Min(0.7f, currentW1 + 0.1f)}",
                });
            }

            if (avgParseSuccessRate < 0.8f && store != null)
            {
                float currentReserve = store.Get("ReserveForOutput");
                recommendations.Add(new AnalysisRecommendationEntry
                {
                    Target = "ReserveForOutput",
                    CurrentValue = currentReserve,
                    RecommendedValue = currentReserve * 1.3f,
                    Confidence = 0.7f,
                    Reason = $"Parse success rate {avgParseSuccessRate:P0}, increase output reserve",
                    TriggerRule = "ParseSuccessRate < 0.8",
                    ComputationDetail = $"Current={currentReserve}, Recommended={currentReserve}*1.3={currentReserve * 1.3f}",
                });
            }

            var report = new AnalysisReportRecord
            {
                AnalysisWindow = $"last_{records.Count}_records",
                TotalRecords = records.Count,
                ComputedMetrics = metrics,
                Recommendations = recommendations.OfType<AnalysisRecommendationEntry>().ToList(),
                GeneratedAtTicks = DateTime.Now.Ticks,
            };

            ApplyAutoApplyMode(report, store!);

            FlywheelAnalysisReportWriter.Write(report);

            return recommendations;
        }

        private static void ApplyAutoApplyMode(AnalysisReportRecord report, FlywheelParameterStore store)
        {
            var mode = RimMindCoreMod.Settings?.autoApplyMode ?? FlywheelAutoApplyMode.Off;
            float threshold = RimMindCoreMod.Settings?.autoApplyConfidenceThreshold ?? 0.8f;

            if (mode == FlywheelAutoApplyMode.Off)
                return;

            foreach (var rec in report.Recommendations)
            {
                if (mode == FlywheelAutoApplyMode.LogOnly)
                {
                    if (RimMindCoreMod.Settings?.debugLogging == true)
                        Log.Message($"[RimMind] Flywheel recommendation: {rec.Target} {rec.CurrentValue} -> {rec.RecommendedValue} (confidence={rec.Confidence}, reason={rec.Reason})");
                    continue;
                }

                if (mode == FlywheelAutoApplyMode.ApplyWithLog && rec.Confidence >= threshold && store != null)
                {
                    store.UpdateParameter(rec.Target, rec.RecommendedValue);
                    rec.Applied = true;
                    rec.ApplyTimestampTicks = DateTime.Now.Ticks;
                    if (RimMindCoreMod.Settings?.debugLogging == true)
                        Log.Message($"[RimMind] Flywheel auto-applied: {rec.Target} = {rec.RecommendedValue}");
                }
            }
        }

        private static float ComputeAvgBudgetUtilization(List<TelemetryRecord> records)
        {
            float sum = 0;
            int count = 0;
            foreach (var r in records)
            {
                if (r.BudgetValue > 0 && r.TotalTokens > 0)
                {
                    float budgetLimit = r.BudgetValue * 4000f;
                    if (budgetLimit > 0)
                    {
                        sum += r.TotalTokens / budgetLimit;
                        count++;
                    }
                }
            }
            return count > 0 ? sum / count : 0f;
        }

        private static float ComputeAvgCacheHitRate(List<TelemetryRecord> records)
        {
            float sum = 0;
            int count = 0;
            foreach (var r in records)
            {
                if (r.CacheHitRate != null && r.CacheHitRate.Count > 0)
                {
                    foreach (var kvp in r.CacheHitRate)
                    {
                        sum += kvp.Value;
                        count++;
                    }
                }
            }
            return count > 0 ? sum / count : 0f;
        }

        private static float ComputeAvgParseSuccessRate(List<TelemetryRecord> records)
        {
            int success = 0;
            int total = 0;
            foreach (var r in records)
            {
                total++;
                if (r.ResponseParseSuccess)
                    success++;
            }
            return total > 0 ? (float)success / total : 1f;
        }

        private static float ComputeAvgTrimRatio(List<TelemetryRecord> records)
        {
            float sum = 0;
            int count = 0;
            foreach (var r in records)
            {
                int included = r.KeysIncluded?.Length ?? 0;
                int trimmed = r.KeysTrimmed?.Length ?? 0;
                int total = included + trimmed;
                if (total > 0)
                {
                    sum += (float)trimmed / total;
                    count++;
                }
            }
            return count > 0 ? sum / count : 0f;
        }
    }
}
