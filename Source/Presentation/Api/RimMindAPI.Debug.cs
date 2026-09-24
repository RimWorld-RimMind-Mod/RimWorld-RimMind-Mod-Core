using RimMind.Presentation.Runtime;
using RimMind.Presentation.Runtime.Services;
using RimMind.Application.Common.Interfaces.UI;

namespace RimMind.Presentation.Api
{
    public static partial class RimMindAPI
    {
        public static class Debug
        {
            private static readonly RuntimeServiceRef<IWindowService> Windows =
                RuntimeServiceRef<IWindowService>.Optional();

            public static void OpenAIRequests()
            {
                Windows.ValueOrDefault?.OpenAIRequests();
            }
        }
    }
}
