using System.Collections.Generic;
using RimMind.Application.Common.Interfaces.Client;
using RimMind.Application.Common.Interfaces.Pipeline;
using RimMind.Application.Common.Models.Context;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Application.Common.Models.Tools;
using RimMind.Domain.Agent.Modes;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Common.Models.Pipeline
{
    public sealed class LlmRequestContext : PipelineContextBase
    {
        public LlmRequestEnvelope Envelope { get; set; } = null!;
        public Result<LlmResponse, RimMindError>? Result { get; set; }

        // Middleware inter-pass data
        public ContextSnapshot? Snapshot { get; set; }
        public IAIClient? Client { get; set; }
        public bool CacheHit { get; set; }
        public string? CacheKey { get; set; }
        public int RetryCount { get; set; }

        // ToolCall agentic loop data
        public IReadOnlyList<ToolResult>? ToolCallResults { get; set; }
        public int ToolCallRound { get; set; }

        // Agent mode context for mode-aware middleware
        public AgentModeId AgentModeId { get; set; }

        public LlmRequestContext() : base() { }

        public LlmRequestContext(LlmRequestEnvelope envelope, string? traceId = null, System.Threading.CancellationToken ct = default)
            : base(traceId ?? envelope.TraceId, ct)
        {
            Envelope = envelope;
        }
    }
}
