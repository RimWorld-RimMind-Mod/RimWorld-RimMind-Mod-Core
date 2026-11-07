using System.Collections.Generic;
using Newtonsoft.Json;

#pragma warning disable CS0649

namespace RimMind.Infrastructure.Services.Clients.Player2
{
    internal class Player2MessageDto
    {
        [JsonProperty("role")]
        public string Role = "";

        [JsonProperty("content")]
        public string? Content = "";

        [JsonProperty("reasoning_content", NullValueHandling = NullValueHandling.Ignore)]
        public string? ReasoningContent;

        [JsonProperty("tool_call_id", NullValueHandling = NullValueHandling.Ignore)]
        public string? ToolCallId;

        [JsonProperty("tool_calls", NullValueHandling = NullValueHandling.Ignore)]
        public List<Player2ToolCallDto>? ToolCalls;
    }

    internal class Player2ToolCallDto
    {
        [JsonProperty("id")]
        public string Id = "";

        [JsonProperty("type")]
        public string Type = "function";

        [JsonProperty("function")]
        public Player2ToolCallFunctionDto? Function;
    }

    internal class Player2ToolCallFunctionDto
    {
        [JsonProperty("name")]
        public string Name = "";

        [JsonProperty("arguments")]
        public string Arguments = "";
    }
}
