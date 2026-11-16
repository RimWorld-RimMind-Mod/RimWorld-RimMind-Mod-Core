using System;

namespace RimMind.Domain.Audio
{
    /// <summary>
    /// Synthesized audio buffer and format metadata returned by <c>ISpeechSynthesizer</c>.
    /// </summary>
    public sealed class SpeechSynthesisResult
    {
        public byte[] AudioBytes { get; init; } = Array.Empty<byte>();
        public AudioContainerFormat Format { get; init; } = AudioContainerFormat.Wav;
        public int SampleRateHz { get; init; } = 24000;
        public int Channels { get; init; } = 1;
        public int BitsPerSample { get; init; } = 16;
        public string ProviderId { get; init; } = string.Empty;
    }
}
