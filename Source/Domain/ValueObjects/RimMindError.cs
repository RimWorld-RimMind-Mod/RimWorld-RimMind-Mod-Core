using System;
using System.Collections.Generic;

namespace RimMind.Domain.ValueObjects
{
    public sealed record RimMindError
    {
        public RimMindErrorCode Code { get; init; }
        public string Message { get; init; }
        public string? TraceId { get; init; }
        public string? Source { get; init; }
        public IReadOnlyDictionary<string, object?>? Details { get; init; }
        public Exception? InnerException { get; init; }

        public RimMindError(RimMindErrorCode code, string message)
        {
            Code = code;
            Message = message;
        }

        public override string ToString()
            => $"[{Code}] {Message}{(TraceId != null ? $" (trace={TraceId})" : "")}";
    }
}
