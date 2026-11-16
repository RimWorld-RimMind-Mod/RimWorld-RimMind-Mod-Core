namespace RimMind.Domain.Audio
{
    /// <summary>
    /// Transcribed text and timing metadata returned by <c>ISpeechRecognizer</c>.
    /// </summary>
    public sealed class SpeechRecognitionResult
    {
        public string RequestId { get; init; } = string.Empty;
        public string Text { get; init; } = string.Empty;
        public int DurationMs { get; init; }
        public string ProviderId { get; init; } = string.Empty;
    }
}
