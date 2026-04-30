namespace RimMind.Core.Client
{
    public class AIResponse
    {
        public bool Success { get; set; }
        public string Content { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
        public int TokensUsed { get; set; }
        public int PromptTokens;
        public int CompletionTokens;
        public int CachedTokens;
        public string RequestId { get; set; } = string.Empty;

        public AIRequestState State { get; set; } = AIRequestState.Queued;
        public AIRequestPriority Priority { get; set; } = AIRequestPriority.Normal;
        public int AttemptCount { get; set; } = 1;
        public long QueueWaitMs { get; set; }
        public long ProcessingMs { get; set; }
        public long HttpStatusCode { get; set; }
        public int RequestPayloadBytes { get; set; }
        public string? ToolCallsJson;
        public string? ReasoningContent;

        public static AIResponse Failure(string requestId, string error) => new AIResponse
        {
            Success = false,
            Error = error,
            RequestId = requestId,
            State = AIRequestState.Error
        };

        public static AIResponse Ok(string requestId, string content, int tokens) => new AIResponse
        {
            Success = true,
            Content = content,
            TokensUsed = tokens,
            RequestId = requestId,
            State = AIRequestState.Completed
        };

        public static AIResponse Cancelled(string requestId, string reason) => new AIResponse
        {
            Success = false,
            Error = reason,
            RequestId = requestId,
            State = AIRequestState.Cancelled
        };
    }
}
