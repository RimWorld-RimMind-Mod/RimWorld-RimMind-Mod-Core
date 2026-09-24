using System.Collections.Generic;
using Newtonsoft.Json;

#pragma warning disable CS0649

namespace RimMind.Infrastructure.Services.Clients.OpenAI
{
    internal class OpenAIRequestDto
    {
        public string model = "";
        public List<MessageDto>? messages;
        public int max_tokens;
        public float temperature;
        public bool stream;
        public ResponseFormatDto? response_format;
        public List<ToolDto>? tools;
        public object? tool_choice;
    }

    internal class OpenAIResponseDto
    {
        public List<ChoiceDto>? choices;
        public UsageDto? usage;
    }

    internal class ChoiceDto
    {
        public int index;
        public MessageDto? message;
        public string? finish_reason;
    }

    internal class UsageDto
    {
        public int prompt_tokens;
        public int completion_tokens;
        public int total_tokens;
        public PromptTokensDetailsDto? prompt_tokens_details;
    }

    internal class PromptTokensDetailsDto
    {
        public int cached_tokens;
    }

    internal class ResponseFormatDto
    {
        public string type = "";
        public object? json_schema;
    }

    internal class ToolDto
    {
        public string type = "function";
        [JsonProperty("function")]
        public ToolFunctionDto? Function;
    }

    internal class ToolFunctionDto
    {
        [JsonProperty("name")]
        public string Name = "";
        [JsonProperty("description")]
        public string Description = "";
        [JsonProperty("parameters")]
        public object? Parameters;
    }

    internal class ToolCallFunctionDto
    {
        [JsonProperty("name")]
        public string Name = "";
        [JsonProperty("arguments")]
        public string Arguments = "";
    }

    internal class MessageDto
    {
        public string role = "";
        public string? content;
        public string? reasoning_content;
        public string? name;
        public string? tool_call_id;
        public List<ToolCallDto>? tool_calls;

        [JsonProperty("function_call")]
        public object? function_call;
    }

    internal class ToolCallDto
    {
        [JsonProperty("id")]
        public string Id = "";
        [JsonProperty("type")]
        public string Type = "function";
        [JsonProperty("function")]
        public ToolCallFunctionDto? Function;
    }

    internal class OpenAIStreamChunkDto
    {
        public List<OpenAIStreamChoiceDto>? choices;
        public UsageDto? usage;
    }

    internal class OpenAIStreamChoiceDto
    {
        public int index;
        public OpenAIStreamDeltaDto? delta;
        public string? finish_reason;
    }

    internal class OpenAIStreamDeltaDto
    {
        public string? role;
        public string? content;
        public string? reasoning_content;
        public List<ToolCallDto>? tool_calls;
    }
}
