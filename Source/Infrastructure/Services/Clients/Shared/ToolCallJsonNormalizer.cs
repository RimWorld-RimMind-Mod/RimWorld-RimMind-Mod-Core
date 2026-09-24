using System.Collections.Generic;
using Newtonsoft.Json;

namespace RimMind.Infrastructure.Services.Clients.Shared
{
    /// <summary>
    /// Normalizes tool call data from different provider DTOs into a unified JSON format.
    /// Eliminates duplicate anonymous-type projection across OpenAI and Player2 clients.
    /// </summary>
    internal static class ToolCallJsonNormalizer
    {
        /// <summary>
        /// Converts tool call entries to the standardized JSON format used by LlmResponse.ToolCallsJson.
        /// Returns null if the collection is null or empty.
        /// </summary>
        public static string? Normalize(IEnumerable<ToolCallEntry>? toolCalls)
        {
            if (toolCalls == null) return null;
            var list = new List<ToolCallEntry>(toolCalls);
            if (list.Count == 0) return null;

            var converted = list.ConvertAll(tc => new
            {
                id = tc.Id,
                type = tc.Type,
                function = new
                {
                    name = tc.FunctionName,
                    arguments = tc.FunctionArguments,
                }
            });
            return JsonConvert.SerializeObject(converted);
        }
    }

    /// <summary>
    /// Provider-agnostic tool call data used by <see cref="ToolCallJsonNormalizer"/>.
    /// </summary>
    internal readonly struct ToolCallEntry
    {
        public string Id { get; init; }
        public string Type { get; init; }
        public string? FunctionName { get; init; }
        public string? FunctionArguments { get; init; }
    }
}
