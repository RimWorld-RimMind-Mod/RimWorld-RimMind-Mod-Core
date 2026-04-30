using System.Collections.Generic;
using Newtonsoft.Json;

namespace RimMind.Core.Client.Player2
{
    internal class Player2RequestDto
    {
        [JsonProperty("model")]
        public string Model { get; set; } = string.Empty;

        [JsonProperty("messages")]
        public List<Player2MessageDto> Messages { get; set; } = new List<Player2MessageDto>();

        [JsonProperty("max_tokens")]
        public int MaxTokens { get; set; }

        [JsonProperty("temperature")]
        public float Temperature { get; set; }

        [JsonProperty("stream")]
        public bool Stream { get; set; }
    }

    internal class Player2MessageDto
    {
        [JsonProperty("role")]
        public string Role { get; set; } = string.Empty;

        [JsonProperty("content")]
        public string Content { get; set; } = string.Empty;
    }

    internal class Player2ResponseDto
    {
        [JsonProperty("choices")]
        public List<Player2ChoiceDto>? Choices { get; set; }

        [JsonProperty("usage")]
        public Player2UsageDto? Usage { get; set; }
    }

    internal class Player2ChoiceDto
    {
        [JsonProperty("message")]
        public Player2AssistantMessageDto? Message { get; set; }
    }

    internal class Player2AssistantMessageDto
    {
        [JsonProperty("content")]
        public string Content { get; set; } = string.Empty;

        [JsonProperty("tool_calls")]
        public List<Player2ToolCallDto>? ToolCalls { get; set; }
    }

    internal class Player2ToolCallDto
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("type")]
        public string Type { get; set; } = string.Empty;

        [JsonProperty("function")]
        public Player2ToolCallFunctionDto? Function { get; set; }
    }

    internal class Player2ToolCallFunctionDto
    {
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("arguments")]
        public string Arguments { get; set; } = string.Empty;
    }

    internal class Player2UsageDto
    {
        [JsonProperty("prompt_tokens")]
        public int PromptTokens { get; set; }

        [JsonProperty("completion_tokens")]
        public int CompletionTokens { get; set; }

        [JsonProperty("total_tokens")]
        public int TotalTokens { get; set; }

        [JsonProperty("prompt_tokens_details")]
        public Player2PromptTokensDetailsDto? PromptTokensDetails { get; set; }
    }

    internal class Player2PromptTokensDetailsDto
    {
        [JsonProperty("cached_tokens")]
        public int CachedTokens { get; set; }
    }

    internal class Player2LocalLoginResponse
    {
        [JsonProperty("p2Key")]
        public string P2Key { get; set; } = string.Empty;
    }

    internal class Player2JoulesBalance
    {
        [JsonProperty("balance")]
        public float Balance { get; set; }

        [JsonProperty("currency")]
        public string Currency { get; set; } = string.Empty;
    }
}
