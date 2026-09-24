using System;
using System.Collections.Generic;

namespace RimMind.Application.Common.Models.Diagnostics
{
    public sealed class SpanRecord
    {
        public string SpanId { get; init; } = Guid.NewGuid().ToString("N").Substring(0, 8);
        public string Name { get; init; } = "";
        public string? ParentId { get; init; }
        public DateTime StartTime { get; init; } = DateTime.UtcNow;
        public DateTime? EndTime { get; set; }
        public Dictionary<string, object> Attributes { get; init; } = new();
        public Exception? Exception { get; set; }
        public long DurationMs => EndTime.HasValue ? (long)(EndTime.Value - StartTime).TotalMilliseconds : -1;
    }
}
