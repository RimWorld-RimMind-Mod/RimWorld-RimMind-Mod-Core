using System;
using System.Collections.Generic;
using System.Linq;
using RimMind.Domain.Llm;

namespace RimMind.Application.Features.Llm
{
    public sealed class PromptZonePartition
    {
        public IReadOnlyList<string> StaticSystemPrompts { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> VolatileObservations { get; init; } = Array.Empty<string>();
        public IReadOnlyList<ChatMessage> Examples { get; init; } = Array.Empty<ChatMessage>();
        public IReadOnlyList<ChatMessage> LiveNonSystemMessages { get; init; } = Array.Empty<ChatMessage>();
        public ChatMessage? LastUserMessage { get; init; }

        public string? CombinedStaticSystemPrompt =>
            StaticSystemPrompts.Count > 0 ? string.Join("\n\n", StaticSystemPrompts) : null;

        public string? CombinedVolatileObservation =>
            VolatileObservations.Count > 0 ? string.Join("\n\n", VolatileObservations) : null;
    }

    /// <summary>
    /// Deterministic 4-Zone KV-cache partitioner separating byte-stable L0/L1 system prefixes
    /// from volatile L2/L3/L5 real-time observations and live conversation turns.
    /// Shared across OpenAI, Anthropic, and DeepSeek protocol serializers.
    /// </summary>
    public static class PromptZonePartitioner
    {
        public static bool IsVolatileLayerTag(string? layerTag)
        {
            return string.Equals(layerTag, "L2", StringComparison.OrdinalIgnoreCase)
                || string.Equals(layerTag, "L3", StringComparison.OrdinalIgnoreCase)
                || string.Equals(layerTag, "L5", StringComparison.OrdinalIgnoreCase);
        }

        public static PromptZonePartition Partition(LlmRequestEnvelope? envelope)
        {
            if (envelope == null)
                return new PromptZonePartition();

            var staticSystem = new List<string>();
            var volatileObs = new List<string>();
            var liveNonSystem = new List<ChatMessage>();

            if (envelope.Messages != null)
            {
                for (int i = 0; i < envelope.Messages.Count; i++)
                {
                    var m = envelope.Messages[i];
                    if (m == null) continue;

                    if (string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.IsNullOrWhiteSpace(m.Content))
                            continue;

                        if (IsVolatileLayerTag(m.LayerTag))
                            volatileObs.Add(m.Content);
                        else
                            staticSystem.Add(m.Content);
                    }
                    else
                    {
                        liveNonSystem.Add(m);
                    }
                }
            }

            var examples = envelope.Examples != null
                ? envelope.Examples.Where(ex => ex != null && !string.Equals(ex.Role, "system", StringComparison.OrdinalIgnoreCase)).ToList()
                : new List<ChatMessage>();

            var lastUser = liveNonSystem.LastOrDefault(m => string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase));

            return new PromptZonePartition
            {
                StaticSystemPrompts = staticSystem,
                VolatileObservations = volatileObs,
                Examples = examples,
                LiveNonSystemMessages = liveNonSystem,
                LastUserMessage = lastUser
            };
        }
    }
}
