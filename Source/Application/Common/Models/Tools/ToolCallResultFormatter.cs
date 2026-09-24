using System.Collections.Generic;
using System.Linq;

namespace RimMind.Application.Common.Models.Tools
{
    /// <summary>
    /// Formats ToolCall execution results as context text for agentic loop follow-up.
    /// </summary>
    public static class ToolCallResultFormatter
    {
        /// <summary>
        /// Formats ToolCall results as a context section string.
        /// Output format:
        /// [ToolCall Results (Round {round})]
        /// - Tool: {toolName} -> Result: {content}
        /// - Tool: {toolName} -> Error: {errorMessage}
        /// </summary>
        public static string Format(IReadOnlyList<ToolResult> results, int round)
        {
            if (results == null || results.Count == 0) return "";

            var lines = new List<string> { $"[ToolCall Results (Round {round})]" };
            foreach (var r in results)
            {
                var toolName = r.ToolName ?? "unknown";
                if (r.IsError)
                    lines.Add($"- Tool: {toolName} -> Error: {r.Content}");
                else
                    lines.Add($"- Tool: {toolName} -> Result: {r.Content}");
            }
            return string.Join("\n", lines);
        }
    }
}
