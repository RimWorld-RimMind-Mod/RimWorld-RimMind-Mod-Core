using RimMind.Application.Common.Helpers;
using RimMind.Application.Common.Interfaces.Client;
using RimMind.Application.Common.Interfaces.Context;
using RimMind.Application.Common.Interfaces.Flywheel;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Models.Context;
using RimMind.Presentation.Runtime;
using RimMind.Presentation.Runtime.Services;

namespace RimMind.Presentation.Api
{
    public static partial class RimMindAPI
    {
        public static class Settings
        {
            private static readonly RuntimeServiceRef<ISettingsProvider> SettingsProviders =
                RuntimeServiceRef<ISettingsProvider>.Optional();
            private static readonly RuntimeServiceRef<IHistoryManager> HistoryManagers =
                RuntimeServiceRef<IHistoryManager>.Required();
            private static readonly RuntimeServiceRef<IContextEngine> ContextEngines =
                RuntimeServiceRef<IContextEngine>.Required();
            private static readonly RuntimeServiceRef<ITelemetryCollector> TelemetryCollectors =
                RuntimeServiceRef<ITelemetryCollector>.Required();

            private static ISettingsProvider? GetSettingsProvider()
                => SettingsProviders.ValueOrDefault;

            public static bool IsConfigured() => GetSettingsProvider()?.IsConfigured == true;

            public static IContextSettings? ContextSettings => GetSettingsProvider()?.Context;

            public static bool DebugLogging => GetSettingsProvider()?.DebugLogging == true;
            public static float ActivityFrequencyScale => GetSettingsProvider()?.ActivityFrequencyScale ?? 1.0f;

            public static int ScaleCooldown(int baseCooldownTicks, int minFloorTicks = 0)
                => ActivityCooldownScaler.Scale(baseCooldownTicks, ActivityFrequencyScale, minFloorTicks);

            internal static IHistoryManager GetHistoryManager() => HistoryManagers.Value;
            public static IContextEngine GetContextEngine() => ContextEngines.Value;
            internal static IBudgetScheduler? GetContextScheduler() => ContextEngines.Value.GetScheduler();
            internal static EmbeddingSnapshotStore? GetEmbeddingSnapshotStore() => ContextEngines.Value.GetEmbeddingSnapshotStore();
            public static ITelemetryCollector Telemetry => TelemetryCollectors.Value;
        }
    }
}
