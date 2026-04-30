using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using RimMind.Core.Settings;
using Verse;

namespace RimMind.Core.Flywheel
{
    public class AnalysisRecommendationEntry : ParameterRecommendation
    {
        public bool Applied;
        public long ApplyTimestampTicks;
        public string TriggerRule = null!;
        public string ComputationDetail = null!;
    }

    public class AnalysisReportRecord
    {
        public string AnalysisWindow = null!;
        public int TotalRecords;
        public Dictionary<string, float> ComputedMetrics = null!;
        public List<AnalysisRecommendationEntry> Recommendations = null!;
        public long GeneratedAtTicks;
    }

    public static class FlywheelAnalysisReportWriter
    {
        public static void Write(AnalysisReportRecord report)
        {
            try
            {
                string? settingsPath = RimMindCoreMod.Settings?.analysisReportPath;
                string dir = !string.IsNullOrWhiteSpace(settingsPath)
                    ? settingsPath!
                    : Path.Combine(GenFilePaths.SaveDataFolderPath, "Telemetry", "Analysis");
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string fileName = $"analysis-{DateTime.Now:yyyy-MM-dd-HHmmss}.jsonl";
                string filePath = Path.Combine(dir, fileName);

                string json = JsonConvert.SerializeObject(report, Formatting.None);
                File.AppendAllText(filePath, json + Environment.NewLine);
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimMind] AnalysisReport write failed: {ex.Message}");
            }
        }
    }
}
