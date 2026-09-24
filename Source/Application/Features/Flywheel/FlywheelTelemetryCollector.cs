using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Flywheel;
using RimMind.Application.Common.Models;
using RimMind.Application.Common.Models.Flywheel;

namespace RimMind.Application.Features.Flywheel
{
    public sealed class FlywheelTelemetryCollector : ITelemetryCollector, IDisposable
    {
        private readonly ConcurrentQueue<TelemetryRecord> _records
            = new ConcurrentQueue<TelemetryRecord>();
        private readonly ILogSink? _log;
        private const int MaxRecords = RimMindDefaults.TelemetryMaxRecords;

        public FlywheelTelemetryCollector(ILogSink? log = null) { _log = log; }

        public void Record(string metric, float value, Dictionary<string, string>? tags = null)
        {
            var record = new TelemetryRecord
            {
                Metric = metric,
                Value = value,
                TimestampTicks = DateTime.UtcNow.Ticks,
                Tags = tags
            };
            _records.Enqueue(record);
            while (_records.Count > MaxRecords)
                _records.TryDequeue(out _);
        }

        public List<TelemetryRecord> GetRecent(int count = RimMindDefaults.TelemetryRecordLimit)
        {
            var result = new List<TelemetryRecord>();
            foreach (var r in _records)
            {
                result.Add(r);
                if (result.Count >= count) break;
            }
            return result;
        }

        public Dictionary<string, float> ComputeAggregates()
        {
            var aggregates = new Dictionary<string, float>();
            var byMetric = new Dictionary<string, List<float>>();
            foreach (var r in _records)
            {
                if (!byMetric.ContainsKey(r.Metric))
                    byMetric[r.Metric] = new List<float>();
                byMetric[r.Metric].Add(r.Value);
            }
            foreach (var kvp in byMetric)
            {
                if (kvp.Value.Count > 0)
                {
                    var sum = 0f;
                    foreach (var v in kvp.Value) sum += v;
                    aggregates[$"{kvp.Key}_avg"] = sum / kvp.Value.Count;
                    aggregates[$"{kvp.Key}_last"] = kvp.Value[kvp.Value.Count - 1];
                    aggregates[$"{kvp.Key}_count"] = kvp.Value.Count;
                }
            }
            return aggregates;
        }

        public void Clear()
        {
            while (_records.TryDequeue(out _)) { }
        }

        public List<TelemetryRecord> GetRecentRecords(int count = RimMindDefaults.TelemetryRecordLimit) => GetRecent(count);

        public void Dispose()
        {
            Clear();
        }
    }

}
