using System.Collections.Generic;

namespace RimMind.Application.Common.Models.Debug
{
    public enum AIRequestTraceState
    {
        Running,
        Completed,
        Failed
    }

    public sealed record AIRequestToolCallTrace(
        string ToolCallId,
        string ToolName,
        bool Succeeded,
        string? Error);

    public sealed class AIRequestTraceEntry
    {
        public string RequestId { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public string SystemPrompt { get; set; } = string.Empty;
        public string UserPrompt { get; set; } = string.Empty;
        public string AssistantPrompt { get; set; } = string.Empty;
        public string Response { get; set; } = string.Empty;
        public string? Error { get; set; }
        public int TokensUsed { get; set; }
        public int ElapsedMs { get; set; }
        public System.DateTime StartedAtUtc { get; set; }
        public System.DateTime? FinishedAtUtc { get; set; }
        public AIRequestTraceState State { get; set; } = AIRequestTraceState.Running;
        public List<AIRequestToolCallTrace> ToolCalls { get; } = new();
    }
}
