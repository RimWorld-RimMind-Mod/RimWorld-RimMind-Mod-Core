using System;
using System.Collections.Generic;
using System.Threading;
using RimMind.Application.Common.Interfaces.Pipeline;

namespace RimMind.Application.Common.Models.Pipeline
{
    public abstract class PipelineContextBase : IPipelineContext
    {
        public string TraceId { get; }
        public DateTime StartTimeUtc { get; } = DateTime.UtcNow;
        public bool IsShortCircuited { get; private set; }
        public string? ShortCircuitReason { get; private set; }
        private readonly Dictionary<string, object?> _items = new Dictionary<string, object?>();
        public IDictionary<string, object?> Items => _items;
        public CancellationToken Ct { get; }

        protected PipelineContextBase(string? traceId = null, CancellationToken ct = default)
        {
            TraceId = traceId ?? Guid.NewGuid().ToString("N").Substring(0, 8);
            Ct = ct;
        }

        public void ShortCircuit(string reason)
        {
            IsShortCircuited = true;
            ShortCircuitReason = reason;
        }
    }
}
