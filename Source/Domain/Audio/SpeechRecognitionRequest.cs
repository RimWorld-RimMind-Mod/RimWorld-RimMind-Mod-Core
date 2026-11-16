using System;
using System.Threading;

namespace RimMind.Domain.Audio
{
    /// <summary>
    /// Provider-agnostic request payload for speech-to-text recognition.
    /// </summary>
    public sealed class SpeechRecognitionRequest
    {
        public byte[] AudioBytes { get; init; } = Array.Empty<byte>();
        public AudioContainerFormat Format { get; init; } = AudioContainerFormat.Wav;
        public string FileName { get; init; } = "audio.wav";
        public string? VocabularyHints { get; init; }
        public CancellationToken Ct { get; init; } = default;
    }
}
