using System.Collections.Generic;
using Newtonsoft.Json;

namespace RimMind.Core.Client.OpenAI
{
    internal class OpenAIRequestDto
    {
        public string model { get; set; } = string.Empty;
        public List<MessageDto> messages { get; set; } = new List<MessageDto>();
        public int max_tokens { get; set; }
        public float temperature { get; set; }
        public bool stream { get; set; }
        public ResponseFormatDto? response_format { get; set; }
        public List<ToolDto>? tools { get; set; }
        public object? tool_choice { get; set; }
    }

    internal class ToolDto
    {
        [JsonProperty("type")]
        public string Type { get; set; } = "function";

        [JsonProperty("function")]
        public ToolFunctionDto? Function { get; set; }
    }

    internal class ToolFunctionDto
    {
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("description")]
        public string Description { get; set; } = string.Empty;

        [JsonProperty("parameters")]
        public object? Parameters { get; set; }
    }

    internal class MessageDto
    {
        public string role { get; set; } = string.Empty;
        public string? content { get; set; }
        public string? reasoning_content { get; set; }
        public string? tool_call_id { get; set; }
        public List<ToolCallDto>? tool_calls { get; set; }
    }

    internal class ToolCallDto
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("type")]
        public string Type { get; set; } = "function";

        [JsonProperty("function")]
        public ToolCallFunctionDto? Function { get; set; }
    }

    internal class ToolCallFunctionDto
    {
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("arguments")]
        public string Arguments { get; set; } = string.Empty;
    }

    internal class ResponseFormatDto
    {
        public string type { get; set; } = "json_object";

        public object? json_schema { get; set; }
    }

    internal class OpenAIResponseDto
    {
        public List<ChoiceDto>? choices { get; set; }
        public UsageDto? usage { get; set; }
    }

    internal class ChoiceDto
    {
        public AssistantMessageDto? message { get; set; }
    }

    internal class AssistantMessageDto
    {
        public string content { get; set; } = string.Empty;
        public string? reasoning_content { get; set; }
        public List<ToolCallDto>? tool_calls { get; set; }
    }

    internal class UsageDto
    {
        public int prompt_tokens { get; set; }
        public int completion_tokens { get; set; }
        public int total_tokens { get; set; }
        public PromptTokensDetailsDto? prompt_tokens_details { get; set; }
    }

    internal class PromptTokensDetailsDto
    {
        public int cached_tokens { get; set; }
    }
}
