using System.Collections.Generic;

namespace RimMind.Core.Client.OpenAI
{
    // ── 请求 DTO ──────────────────────────────────────────────────────────────

    internal class OpenAIRequestDto
    {
        public string model { get; set; } = string.Empty;
        public List<MessageDto> messages { get; set; } = new List<MessageDto>();
        public int max_tokens { get; set; }
        public float temperature { get; set; }
        public bool stream { get; set; }
        public ResponseFormatDto? response_format { get; set; }
    }

    internal class MessageDto
    {
        public string role { get; set; } = string.Empty;
        public string content { get; set; } = string.Empty;
    }

    internal class ResponseFormatDto
    {
        public string type { get; set; } = "json_object";
    }

    // ── 响应 DTO ──────────────────────────────────────────────────────────────

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
    }

    internal class UsageDto
    {
        public int total_tokens { get; set; }
    }
}
