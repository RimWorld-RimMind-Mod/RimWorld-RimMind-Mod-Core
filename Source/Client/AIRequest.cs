using System.Collections.Generic;

namespace RimMind.Core.Client
{
    public class AIRequest
    {
        public string SystemPrompt { get; set; } = string.Empty;

        public string UserPrompt { get; set; } = string.Empty;

        public List<ChatMessage>? Messages { get; set; }

        public int MaxTokens { get; set; } = 800;
        public float Temperature { get; set; } = 0.7f;

        public string RequestId { get; set; } = string.Empty;

        public string ModId { get; set; } = string.Empty;

        public int ExpireAtTicks { get; set; }

        public bool UseJsonMode { get; set; } = true;

        public AIRequestPriority Priority { get; set; } = AIRequestPriority.Normal;

        public int MaxRetryCount { get; set; } = -1;
    }

    public class ChatMessage
    {
        public string Role { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
    }
}
