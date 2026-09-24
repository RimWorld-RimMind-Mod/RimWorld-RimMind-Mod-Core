using Verse;

namespace RimMind.Presentation.Api
{
    public static partial class RimMindAPI
    {
        /// <summary>
        /// Canonical logging surface for RimMind.
        /// Formats messages with the [RimMind] prefix and routes to <see cref="Verse.Log"/>.
        /// </summary>
        public static class Log
        {
            public static void Message(string message) => Verse.Log.Message($"[RimMind] {message}");
            public static void Warning(string message) => Verse.Log.Warning($"[RimMind] {message}");
            public static void Error(string message) => Verse.Log.Error($"[RimMind] {message}");
        }
    }
}
