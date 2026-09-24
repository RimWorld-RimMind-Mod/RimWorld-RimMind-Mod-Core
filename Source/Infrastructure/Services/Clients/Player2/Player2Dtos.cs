using System.Collections.Generic;
using Newtonsoft.Json;

#pragma warning disable CS0649

namespace RimMind.Infrastructure.Services.Clients.Player2
{
    internal class Player2RequestDto
    {
        public string Model = "default";
        public List<Player2MessageDto> Messages = new List<Player2MessageDto>();
        public float Temperature;
        public int MaxTokens;
        public bool Stream;
        public object? ResponseFormat;
        public List<object>? Tools;
        public object? ToolChoice;
    }

    internal class Player2ResponseDto
    {
        [JsonProperty("choices")]
        public List<Player2ChoiceDto>? Choices;

        [JsonProperty("usage")]
        public Player2UsageDto? Usage;
    }

    internal class Player2ChoiceDto
    {
        [JsonProperty("message")]
        public Player2MessageDto? Message;

        [JsonProperty("finish_reason")]
        public string? FinishReason;
    }

    internal class Player2UsageDto
    {
        [JsonProperty("total_tokens")]
        public int TotalTokens;

        [JsonProperty("prompt_tokens")]
        public int PromptTokens;

        [JsonProperty("completion_tokens")]
        public int CompletionTokens;

        [JsonProperty("prompt_tokens_details")]
        public Player2PromptTokensDetailsDto? PromptTokensDetails;
    }

    internal class Player2PromptTokensDetailsDto
    {
        [JsonProperty("cached_tokens")]
        public int CachedTokens;
    }

    internal class Player2LocalLoginResponse
    {
        [JsonProperty("p2_key")]
        public string? P2Key;

        [JsonProperty("error")]
        public string? Error;
    }

    internal class Player2JoulesBalance
    {
        [JsonProperty("balance")]
        public float Balance;

        [JsonProperty("currency")]
        public string? Currency;
    }

    internal class Player2StreamChunkDto
    {
        [JsonProperty("choices")]
        public List<Player2StreamChoiceDto>? Choices;

        [JsonProperty("usage")]
        public Player2UsageDto? Usage;
    }

    internal class Player2StreamChoiceDto
    {
        [JsonProperty("index")]
        public int Index;

        [JsonProperty("delta")]
        public Player2StreamDeltaDto? Delta;

        [JsonProperty("finish_reason")]
        public string? FinishReason;
    }

    internal class Player2StreamDeltaDto
    {
        [JsonProperty("role")]
        public string? Role;

        [JsonProperty("content")]
        public string? Content;

        [JsonProperty("tool_calls")]
        public List<Player2StreamToolCallDto>? ToolCalls;
    }

    internal class Player2StreamToolCallDto
    {
        [JsonProperty("index")]
        public int Index;

        [JsonProperty("id")]
        public string? Id;

        [JsonProperty("type")]
        public string? Type;

        [JsonProperty("function")]
        public Player2StreamFunctionDto? Function;
    }

    internal class Player2StreamFunctionDto
    {
        [JsonProperty("name")]
        public string? Name;

        [JsonProperty("arguments")]
        public string? Arguments;
    }
}
