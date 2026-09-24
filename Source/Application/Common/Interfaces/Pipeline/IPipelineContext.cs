using System;
using System.Collections.Generic;
using System.Threading;

namespace RimMind.Application.Common.Interfaces.Pipeline
{
    public interface IPipelineContext
    {
        string TraceId { get; }
        DateTime StartTimeUtc { get; }
        bool IsShortCircuited { get; }
        string? ShortCircuitReason { get; }
        IDictionary<string, object?> Items { get; }
        CancellationToken Ct { get; }
        void ShortCircuit(string reason);
    }
}
