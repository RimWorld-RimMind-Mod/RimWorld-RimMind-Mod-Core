using RimMind.Application.Common.Interfaces.UI;
using Verse;

namespace RimMind.Infrastructure.UI
{
    public sealed class NullAudioPlayer : IAudioPlayer
    {
        public void PlayAudio(string audioUrl)
        {
            Log.Message($"[RimMind-Core] NullAudioPlayer: audio playback skipped for {audioUrl}");
        }
    }
}
