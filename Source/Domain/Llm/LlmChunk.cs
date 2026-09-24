namespace RimMind.Domain.Llm
{
    public sealed record LlmChunk
    {
        public string? DeltaContent { get; init; }
        public string? DeltaToolCallsJson { get; init; }
        public string? DeltaReasoningContent { get; init; }
        public int? DeltaPromptTokens { get; init; }
        public int? DeltaCompletionTokens { get; init; }
        public int? DeltaCachedTokens { get; init; }
        public bool IsLast { get; init; }
        public LlmResponse? FinalResponse { get; init; }
    }
}
