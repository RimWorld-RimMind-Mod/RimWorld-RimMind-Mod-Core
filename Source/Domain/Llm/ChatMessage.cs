using System.Collections.Generic;

namespace RimMind.Domain.Llm
{
    public class ChatMessage
    {
        public string Role { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string? ReasoningContent { get; set; }
        public string? ToolCallId { get; set; }
        public List<ChatToolCall>? ToolCalls { get; set; }
        public string? LayerTag { get; set; }
    }
}
