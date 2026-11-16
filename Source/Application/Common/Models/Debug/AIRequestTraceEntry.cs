using System;
using System.Collections.Generic;
using RimMind.Domain.Llm;

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
        string? Error,
        string? ArgumentsJson = null,
        string? ResultContent = null);

    public sealed record AIRequestLayerTrace(
        string Role,
        string LayerTag,
        string Content,
        int CharCount,
        bool IsPrefixReused);

    public sealed record AIRequestActionOutcomeTrace(
        string ActionIntent,
        bool Succeeded,
        string? Detail,
        string? ParamPreview = null);

    public sealed class AIRequestTraceEntry
    {
        public string RequestId { get; set; } = string.Empty;
        public string TraceId { get; set; } = string.Empty;
        public string RolloutId { get; set; } = string.Empty;
        public int TurnIndex { get; set; }
        public string ModId { get; set; } = string.Empty;
        public string ScenarioId { get; set; } = string.Empty;
        public string? NpcId { get; set; }
        public bool IsAgentRollout { get; set; }
        public string Source { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public string SystemPrompt { get; set; } = string.Empty;
        public string UserPrompt { get; set; } = string.Empty;
        public string AssistantPrompt { get; set; } = string.Empty;
        public string Response { get; set; } = string.Empty;
        public string? ReasoningContent { get; set; }
        public string? Error { get; set; }
        public int TokensUsed { get; set; }
        public int PromptTokens { get; set; }
        public int CompletionTokens { get; set; }
        public int CachedTokens { get; set; }
        public int ElapsedMs { get; set; }
        public DateTime StartedAtUtc { get; set; }
        public DateTime? FinishedAtUtc { get; set; }
        public AIRequestTraceState State { get; set; } = AIRequestTraceState.Running;
        public List<AIRequestToolCallTrace> ToolCalls { get; } = new();
        public List<AIRequestLayerTrace> Layers { get; } = new();
        public AIRequestActionOutcomeTrace? ActionOutcome { get; set; }

        /// <summary>
        /// Builds structured layer traces from envelope messages so prompt assembly middleware
        /// and request submission share a single extraction path.
        /// </summary>
        public static List<AIRequestLayerTrace> ExtractLayerTraces(IReadOnlyList<ChatMessage>? messages)
        {
            var layers = new List<AIRequestLayerTrace>();
            if (messages == null || messages.Count == 0)
                return layers;

            foreach (var msg in messages)
            {
                if (msg == null || string.IsNullOrWhiteSpace(msg.Content))
                    continue;

                string role = string.IsNullOrWhiteSpace(msg.Role) ? "user" : msg.Role.ToLowerInvariant();
                string tag = !string.IsNullOrWhiteSpace(msg.LayerTag)
                    ? msg.LayerTag!
                    : (string.Equals(role, "system", StringComparison.OrdinalIgnoreCase) ? "L0" : "L4");
                string content = msg.Content;
                layers.Add(new AIRequestLayerTrace(role, tag, content, content.Length, IsPrefixReused: false));
            }

            return layers;
        }
    }
}
