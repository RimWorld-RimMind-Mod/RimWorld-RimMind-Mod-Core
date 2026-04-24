using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using RimMind.Core.Context;
using RimMind.Core.Settings;
using Verse;

namespace RimMind.Core.Flywheel
{
    public class TelemetryRecord
    {
        public string NpcId;
        public string Scenario;
        public int PromptTokens;
        public int CompletionTokens;
        public int TotalTokens;
        public int CachedTokens;
        public float BudgetValue;
        public string[] KeysIncluded;
        public string[] KeysTrimmed;
        public Dictionary<string, int> LayerTokenBreakdown;
        public long TimestampTicks;
        public Dictionary<string, int> KeyChangeFreq;
        public Dictionary<string, float> CacheHitRate;
        public Dictionary<string, float> ScoreDistribution;
        public int DiffCount;
        public float DiffMergeLatency;
        public bool ResponseParseSuccess;
        public Dictionary<string, long> LatencyByLayerMs;
        public long RequestLatencyMs;
    }

    public class FlywheelTelemetryCollector
    {
        private const int FlushThreshold = 50;
        private const int RecentRecordsCapacity = 200;

        private readonly List<TelemetryRecord> _buffer = new List<TelemetryRecord>();
        private readonly List<TelemetryRecord> _recentRecords = new List<TelemetryRecord>();
        private readonly object _lock = new object();

        public void Record(TelemetryRecord record)
        {
            lock (_lock)
            {
                _buffer.Add(record);
                _recentRecords.Add(record);
                if (_recentRecords.Count > RecentRecordsCapacity)
                    _recentRecords.RemoveRange(0, _recentRecords.Count - RecentRecordsCapacity);
                if (_buffer.Count >= FlushThreshold)
                    FlushInternal();
            }
        }

        public List<TelemetryRecord> GetRecentRecords(int count)
        {
            lock (_lock)
            {
                if (count >= _recentRecords.Count)
                    return new List<TelemetryRecord>(_recentRecords);
                return _recentRecords.GetRange(_recentRecords.Count - count, count);
            }
        }

        public void Flush()
        {
            lock (_lock)
            {
                FlushInternal();
            }
        }

        private void FlushInternal()
        {
            if (_buffer.Count == 0) return;

            var toWrite = new List<TelemetryRecord>(_buffer);
            _buffer.Clear();

            try
            {
                string settingsPath = RimMindCoreMod.Settings?.telemetryDataPath;
                string dir = !string.IsNullOrWhiteSpace(settingsPath)
                    ? settingsPath
                    : Path.Combine(GenFilePaths.SaveDataFolderPath, "Telemetry");
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string fileName = $"{DateTime.Now:yyyy-MM-dd}.jsonl";
                string filePath = Path.Combine(dir, fileName);

                var lines = new List<string>(toWrite.Count);
                foreach (var record in toWrite)
                    lines.Add(JsonConvert.SerializeObject(record, Formatting.None));

                File.AppendAllLines(filePath, lines);
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimMind] Telemetry flush failed: {ex.Message}");
            }
        }

        public void RecordSnapshotBuild(ContextSnapshot snapshot, float budgetValue, string[] includedKeys, string[] trimmedKeys)
        {
            var record = new TelemetryRecord
            {
                NpcId = snapshot.NpcId,
                Scenario = snapshot.Scenario,
                BudgetValue = budgetValue,
                KeysIncluded = includedKeys ?? new string[0],
                KeysTrimmed = trimmedKeys ?? new string[0],
                LayerTokenBreakdown = new Dictionary<string, int>
                {
                    { "L0", snapshot.Meta.L0Tokens },
                    { "L1", snapshot.Meta.L1Tokens },
                    { "L2", snapshot.Meta.L2Tokens },
                    { "L3", snapshot.Meta.L3Tokens },
                    { "L4", snapshot.Meta.L4Tokens },
                },
                KeyChangeFreq = snapshot.KeyChangeCounts.Count > 0
                    ? new Dictionary<string, int>(snapshot.KeyChangeCounts)
                    : null,
                CacheHitRate = ComputeCacheHitRates(snapshot),
                ScoreDistribution = snapshot.KeyScores.Count > 0
                    ? new Dictionary<string, float>(snapshot.KeyScores)
                    : null,
                DiffCount = snapshot.DiffCount,
                LatencyByLayerMs = snapshot.LatencyByLayerMs.Count > 0
                    ? new Dictionary<string, long>(snapshot.LatencyByLayerMs)
                    : null,
                RequestLatencyMs = snapshot.BuildStartTicks > 0
                    ? (DateTime.Now.Ticks - snapshot.BuildStartTicks) / TimeSpan.TicksPerMillisecond
                    : 0,
                TimestampTicks = DateTime.Now.Ticks,
            };
            Record(record);
        }

        private static Dictionary<string, float> ComputeCacheHitRates(ContextSnapshot snapshot)
        {
            if (snapshot.CacheHitEvents.Count == 0) return null;
            var byLayer = new Dictionary<string, List<bool>>();
            foreach (var kvp in snapshot.CacheHitEvents)
            {
                string layer = kvp.Key.StartsWith("L0") ? "L0"
                    : kvp.Key.StartsWith("L1") ? "L1"
                    : kvp.Key.StartsWith("L2") ? "L2"
                    : kvp.Key.StartsWith("L3") ? "L3" : "L4";
                if (!byLayer.ContainsKey(layer))
                    byLayer[layer] = new List<bool>();
                byLayer[layer].Add(kvp.Value);
            }
            var rates = new Dictionary<string, float>();
            foreach (var kvp in byLayer)
            {
                if (kvp.Value.Count > 0)
                    rates[kvp.Key] = (float)kvp.Value.Count(v => v) / kvp.Value.Count;
            }
            return rates.Count > 0 ? rates : null;
        }
    }
}
