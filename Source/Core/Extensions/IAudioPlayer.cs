namespace RimMind.Core.Extensions
{
    public interface IAudioPlayer
    {
        void PlayAudio(string audioUrl);
    }

    public class NullAudioPlayer : IAudioPlayer
    {
        public void PlayAudio(string audioUrl) { }
    }
}
