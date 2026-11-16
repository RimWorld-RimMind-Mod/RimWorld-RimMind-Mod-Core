using System;
using System.Collections.Generic;
using System.Threading;

namespace RimMind.Domain.Llm
{
    public class LlmRequestEnvelope
    {
        // === Identity ===
        public string RequestId { get; init; } = string.Empty;
        public string TraceId { get; init; } = Guid.NewGuid().ToString("N").Substring(0, 12);
        public string ScenarioId { get; init; } = string.Empty;
        public string ModId { get; init; } = string.Empty;

        // === Content ===
        public List<ChatMessage> Messages { get; init; } = new();
        public List<PromptAugmentation>? SystemAugmentations { get; set; }
        public string? JsonSchema { get; init; }
        public List<StructuredTool>? Tools { get; init; }
        public ToolCallDispatchMode ToolDispatchMode { get; init; } = ToolCallDispatchMode.Auto;
        public List<ChatMessage>? Examples { get; init; }
        public int MaxTokens { get; init; } = 800;
        public float Temperature { get; init; } = 0.7f;
        public ReasoningEffort ReasoningEffort { get; init; } = ReasoningEffort.Auto;

        // === Dispatch ===
        public AIRequestPriority Priority { get; init; } = AIRequestPriority.Normal;
        public int? ExpireAtTicks { get; init; }
        public int? MaxRetryCount { get; init; }

        // === Streaming ===
        public bool IsStreaming { get; init; }
        public Action<LlmChunk>? OnStreamChunk { get; init; }
        public CancellationToken Ct { get; init; } = default;

        // === NPC Mode (optional) ===
        public string? NpcId { get; init; }
        public GameStateInfo? GameStateInfo { get; set; }
        public IReadOnlyDictionary<string, string>? Hints { get; init; }

        public LlmRequestEnvelope WithCancellationToken(CancellationToken cancellationToken)
            => Clone(cancellationToken);

        public LlmRequestEnvelope WithTraceAndHints(
            string traceId,
            IReadOnlyDictionary<string, string>? extraHints)
        {
            Dictionary<string, string>? mergedHints = null;
            if (Hints != null && Hints.Count > 0)
            {
                mergedHints = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var kvp in Hints)
                {
                    if (!string.IsNullOrEmpty(kvp.Key) && kvp.Value != null)
                        mergedHints[kvp.Key] = kvp.Value;
                }
            }
            if (extraHints != null && extraHints.Count > 0)
            {
                mergedHints ??= new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var kvp in extraHints)
                {
                    if (!string.IsNullOrEmpty(kvp.Key) && kvp.Value != null)
                        mergedHints[kvp.Key] = kvp.Value;
                }
            }

            return new LlmRequestEnvelope
            {
                RequestId = RequestId,
                TraceId = string.IsNullOrWhiteSpace(traceId) ? TraceId : traceId,
                ScenarioId = ScenarioId,
                ModId = ModId,
                Messages = Messages,
                SystemAugmentations = SystemAugmentations,
                JsonSchema = JsonSchema,
                Tools = Tools,
                ToolDispatchMode = ToolDispatchMode,
                Examples = Examples,
                MaxTokens = MaxTokens,
                Temperature = Temperature,
                ReasoningEffort = ReasoningEffort,
                Priority = Priority,
                ExpireAtTicks = ExpireAtTicks,
                MaxRetryCount = MaxRetryCount,
                IsStreaming = IsStreaming,
                OnStreamChunk = OnStreamChunk,
                Ct = Ct,
                NpcId = NpcId,
                GameStateInfo = GameStateInfo,
                Hints = mergedHints,
            };
        }

        public LlmRequestEnvelope Clone(CancellationToken? cancellationToken = null)
        {            return new LlmRequestEnvelope
            {
                RequestId = RequestId,
                TraceId = TraceId,
                ScenarioId = ScenarioId,
                ModId = ModId,
                Messages = Messages,
                SystemAugmentations = SystemAugmentations,
                JsonSchema = JsonSchema,
                Tools = Tools,
                ToolDispatchMode = ToolDispatchMode,
                Examples = Examples,
                MaxTokens = MaxTokens,
                Temperature = Temperature,
                ReasoningEffort = ReasoningEffort,
                Priority = Priority,
                ExpireAtTicks = ExpireAtTicks,
                MaxRetryCount = MaxRetryCount,
                IsStreaming = IsStreaming,
                OnStreamChunk = OnStreamChunk,
                Ct = cancellationToken ?? Ct,
                NpcId = NpcId,
                GameStateInfo = GameStateInfo,
                Hints = Hints,
            };
        }
    }
}
