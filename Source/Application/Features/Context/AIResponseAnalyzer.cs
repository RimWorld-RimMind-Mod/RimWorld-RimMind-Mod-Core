using System;
using System.Collections.Generic;
using RimMind.Application.Common.Models.Context;
using RimMind.Domain.Llm;

namespace RimMind.Application.Features.Context
{
    /// <summary>
    /// Heuristic analyzer: checks if AI response content contains substrings
    /// from context messages, indicating the AI actually used that context.
    /// Returns the LayerTag values of messages whose content was referenced.
    /// </summary>
    public sealed class AIResponseAnalyzer
    {
        private const int MinSampleLength = 20;
        private const int SampleHeadLength = 50;

        /// <summary>
        /// Extract the keys (LayerTag values) of context messages whose content
        /// appears to have been referenced by the AI response.
        /// </summary>
        public IReadOnlyList<string> ExtractUsedKeys(ContextSnapshot snapshot, LlmResponse response)
        {
            var used = new List<string>();
            if (snapshot == null || response == null) return used;

            var responseText = (response.Content ?? string.Empty) + " " + (response.ToolCallsJson ?? string.Empty);
            if (string.IsNullOrEmpty(responseText)) return used;

            foreach (var msg in snapshot.Messages)
            {
                var content = msg.Content ?? string.Empty;
                if (content.Length < MinSampleLength) continue;
                if (string.IsNullOrEmpty(msg.LayerTag)) continue;

                var sample = content.Substring(0, Math.Min(SampleHeadLength, content.Length));
                if (responseText.IndexOf(sample, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    used.Add(msg.LayerTag!);
                }
            }

            return used;
        }
    }
}
