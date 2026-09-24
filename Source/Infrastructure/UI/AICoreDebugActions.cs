using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Context;
using RimMind.Application.Common.Interfaces.Flywheel;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Interfaces.Mechanisms;
using RimMind.Application.Common.Interfaces.Npc;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Application.Features.Requests.Queue;
using Verse;

namespace RimMind.Infrastructure.UI
{
    [StaticConstructorOnStartup]
    public static partial class RimMindCoreDebugActions
    {
        /// <summary>
        /// Preserves the original composition hook. Actions resolve services when invoked.
        /// </summary>
        public static void Initialize(
            ISettingsProvider? settingsProvider,
            IRequestQueue? requestQueue,
            IClientManager? clientManager,
            IAIDebugLog? debugLog,
            IContextKeyProvider? contextKeyProvider,
            IContextBuilder? contextEngine,
            IProviderRegistry? providerRegistry,
            IContextKeyRegistry? contextKeyRegistry,
            IFlywheelParameterStore? flywheelParameterStore,
            ITelemetryCollector? telemetryCollector,
            IAgentBus? agentBus,
            IHistoryManager? historyManager,
            INpcManager? npcManager,
            IToolRegistry? toolRegistry,
            IGameMechanismRegistry? mechanismRegistry)
        {
            // Kept as a source-compatible composition hook. Debug actions resolve
            // from the lifecycle hubs when invoked and never retain these instances.
        }
    }
}
