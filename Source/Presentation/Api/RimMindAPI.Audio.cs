using RimMind.Application.Common.Interfaces.UI;
using RimMind.Presentation.Runtime;
using RimMind.Presentation.Runtime.Services;

namespace RimMind.Presentation.Api
{
    public static partial class RimMindAPI
    {
        public static class Audio
        {
            private static readonly RuntimeServiceRef<IAudioPlayer> Players =
                RuntimeServiceRef<IAudioPlayer>.Required();

            public static IAudioPlayer AudioPlayer => Players.Value;
        }
    }
}
