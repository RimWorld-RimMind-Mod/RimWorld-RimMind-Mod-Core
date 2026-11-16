using System.Threading;

namespace RimMind.Domain.Audio
{
    /// <summary>
    /// Provider-agnostic request payload for text-to-speech synthesis.
    /// </summary>
    public sealed class SpeechSynthesisRequest
    {
        public string Text { get; init; } = string.Empty;
        public string? VoiceId { get; init; }
        public AudioContainerFormat PreferredFormat { get; init; } = AudioContainerFormat.Wav;
        public bool StripMarkdown { get; init; } = true;
        public CancellationToken Ct { get; init; } = default;
    }
}
