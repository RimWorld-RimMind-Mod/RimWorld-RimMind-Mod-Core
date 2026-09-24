namespace RimMind.Domain.Llm
{
    public sealed class LlmResponse
    {
        public string RequestId { get; init; } = string.Empty;
        public string Content { get; init; } = string.Empty;
        public string? ToolCallsJson { get; init; }
        public string? ReasoningContent { get; init; }
        public int TokensUsed { get; init; }
        public int PromptTokens { get; init; }
        public int CompletionTokens { get; init; }
        public int CachedTokens { get; init; }
        public AIRequestState State { get; init; }
        public AIRequestPriority Priority { get; init; }
        public int AttemptCount { get; init; }
        public long QueueWaitMs { get; init; }
        public long ProcessingMs { get; init; }
        public long HttpStatusCode { get; init; }

        public LlmResponse With(
            string? requestId = null,
            string? content = null,
            string? toolCallsJson = null,
            string? reasoningContent = null,
            int? tokensUsed = null,
            int? promptTokens = null,
            int? completionTokens = null,
            int? cachedTokens = null,
            AIRequestState? state = null,
            AIRequestPriority? priority = null,
            int? attemptCount = null,
            long? queueWaitMs = null,
            long? processingMs = null,
            long? httpStatusCode = null)
        {
            return new LlmResponse
            {
                RequestId = requestId ?? RequestId,
                Content = content ?? Content,
                ToolCallsJson = toolCallsJson ?? ToolCallsJson,
                ReasoningContent = reasoningContent ?? ReasoningContent,
                TokensUsed = tokensUsed ?? TokensUsed,
                PromptTokens = promptTokens ?? PromptTokens,
                CompletionTokens = completionTokens ?? CompletionTokens,
                CachedTokens = cachedTokens ?? CachedTokens,
                State = state ?? State,
                Priority = priority ?? Priority,
                AttemptCount = attemptCount ?? AttemptCount,
                QueueWaitMs = queueWaitMs ?? QueueWaitMs,
                ProcessingMs = processingMs ?? ProcessingMs,
                HttpStatusCode = httpStatusCode ?? HttpStatusCode,
            };
        }
    }
}
