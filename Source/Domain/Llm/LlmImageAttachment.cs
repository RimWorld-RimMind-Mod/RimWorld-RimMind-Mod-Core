namespace RimMind.Domain.Llm
{
    /// <summary>
    /// Engine-free visual attachment carried on dynamic user turns (L4) for multimodal LLM requests.
    /// </summary>
    public sealed class LlmImageAttachment
    {
        public string MediaType { get; init; } = "image/png";
        public string Base64Data { get; init; } = string.Empty;
        public string? Label { get; init; }
        public string? Detail { get; init; }
    }
}
