using System.Collections.Generic;
using RimMind.Application.Common.Models;
using RimMind.Application.Common.Models.Flywheel;

namespace RimMind.Application.Common.Interfaces.Flywheel
{
    public interface ITelemetryCollector
    {
        void Record(string metric, float value, Dictionary<string, string>? tags = null);
        List<TelemetryRecord> GetRecent(int count = RimMindDefaults.TelemetryRecordLimit);
        Dictionary<string, float> ComputeAggregates();
        void Clear();
        List<TelemetryRecord> GetRecentRecords(int count = RimMindDefaults.TelemetryRecordLimit);
    }
}
