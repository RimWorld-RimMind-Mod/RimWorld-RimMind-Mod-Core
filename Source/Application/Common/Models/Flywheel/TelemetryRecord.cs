using System.Collections.Generic;

namespace RimMind.Application.Common.Models.Flywheel
{
    public class TelemetryRecord
    {
        public string Metric { get; set; } = "";
        public float Value { get; set; }
        public long TimestampTicks { get; set; }
        public Dictionary<string, string>? Tags { get; set; }
    }
}
