namespace RimMind.Domain.Audio
{
    /// <summary>
    /// Provider-agnostic audio container / encoding formats for speech synthesis and recognition ports.
    /// </summary>
    public enum AudioContainerFormat
    {
        Wav = 0,
        Pcm16Mono = 1,
        OggOpus = 2,
        Mp3 = 3,
    }
}
