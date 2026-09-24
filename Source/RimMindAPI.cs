using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Interfaces.Agent.Modes;
using RimMind.Application.Common.Interfaces.Client;
using RimMind.Application.Common.Interfaces.Context;
using RimMind.Application.Common.Interfaces.Extension;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Interfaces.Mechanisms;
using RimMind.Application.Common.Interfaces.Npc;
using RimMind.Application.Common.Interfaces.Pipeline;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Application.Common.Interfaces.UI;
using RimMind.Application.Common.Models.Client;
using RimMind.Application.Common.Models.Context;
using RimMind.Application.Common.Models.Npc;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Application.Common.Models.Tools;
using RimMind.Application.Common.Models.UI;
using RimMind.Application.Common.Interfaces.Flywheel;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;
using RimMind.Application.Common.Models.Agent;
using RimMind.Presentation.Runtime;
using RimMind.Presentation.Runtime.Services;
using Verse;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ClientTrackedRequest = RimMind.Application.Common.Models.Client.TrackedRequest;
using UIRequestEntry = RimMind.Application.Common.Models.UI.RequestEntry;

namespace RimMind.Presentation.Api
{
    public static partial class RimMindAPI
    {
        private static readonly RuntimeServiceRef<RimMindRuntime> RuntimeRef =
            RuntimeServiceRef<RimMindRuntime>.Optional();

        private static RimMindRuntime CurrentRuntime =>
            RuntimeRef.ValueOrDefault
            ?? throw new InvalidOperationException("[RimMind-Core] Runtime is not running.");

        [Obsolete("No code consumers in Core or any sub-mod. Scheduled for removal in a future version.")]
        public static void Shutdown()
        {
            var runtime = RuntimeRef.ValueOrDefault;
            if (runtime == null || runtime.IsShutdown) return;
            runtime.Queue.CancelAllRequests();
            RimMindRuntimeHost.Shutdown();
        }

        internal static void ResetForNewGame() => RimMindRuntime.ResetInstance();

        // === Unified Request API (K phase) ===
        public static void Send(LlmRequestEnvelope envelope, Action<Result<LlmResponse, RimMindError>> onComplete)
            => Request.Send(envelope, onComplete);
        public static Task<Result<LlmResponse, RimMindError>> SendAsync(LlmRequestEnvelope envelope)
            => Request.SendAsync(envelope);
        public static void PauseQueue() => Request.PauseQueue();
        public static void ResumeQueue() => Request.ResumeQueue();
        public static int ActiveRequestCount => Request.ActiveRequestCount;
        [Obsolete("No code consumers in Core or any sub-mod. Scheduled for removal in a future version.")]
        public static IReadOnlyList<ClientTrackedRequest> GetActiveRequests() => Request.GetActiveRequests();
        [Obsolete("No code consumers in Core or any sub-mod. Scheduled for removal in a future version.")]
        public static IReadOnlyList<ClientTrackedRequest> GetAllQueuedRequests() => Request.GetAllQueuedRequests();
        public static int TotalQueuedCount => Request.TotalQueuedCount;
        public static void ClearModCooldown(string modId) => Request.ClearModCooldown(modId);
        public static int GetModCooldownTicksLeft(string modId) => Request.GetModCooldownTicksLeft(modId);

        [Obsolete("No code consumers in Core or any sub-mod. Scheduled for removal in a future version.")]
        public static string BuildMapContext(Map map, bool brief = false) => ChatFlow.BuildMapContext(map, brief);

        public static IToolRegistry Tools => ToolSet.Registry;
        public static IGameMechanismRegistry Mechanisms => ToolSet.Mechanisms;
        public static IExtensionRegistry<IAgentMode> Modes => Extensions<IAgentMode>();
        public static IExtensionRegistry<IModeTransitionPolicy> ModePolicies => Extensions<IModeTransitionPolicy>();

        public static IExtensionRegistry<T> Extensions<T>() where T : class, IExtension => Ext.Get<T>();
        public static bool ShouldSkipDialogue(Pawn pawn, string trigger) => Ext.ShouldSkipDialogue(pawn, trigger);
        public static bool ShouldSkipFloatMenu() => Ext.ShouldSkipFloatMenu();
        public static bool ShouldSkipAction(string intentId) => Ext.ShouldSkipAction(intentId);
        public static bool ShouldSkipStorytellerIncident() => Ext.ShouldSkipStorytellerIncident();
        public static void TriggerDialogue(Pawn pawn, string context, Pawn? recipient = null) => Ext.TriggerDialogue(pawn, context, recipient);
        public static void NotifyIncidentExecuted() => Ext.NotifyIncidentExecuted();
        public static bool CanTriggerDialogue => Ext.CanTriggerDialogue;
        public static void RegisterAgentIdentityProvider(Func<Pawn, AgentIdentity?> provider) => Ext.RegisterAgentIdentityProvider(provider);
        [Obsolete("No code consumers in Core or any sub-mod. Scheduled for removal in a future version.")]
        public static AgentIdentity? GetAgentIdentity(Pawn pawn) => Ext.GetAgentIdentity(pawn);
        public static void RegisterAgentActionBridge(IAgentActionBridge bridge) => Ext.RegisterAgentActionBridge(bridge);
        [Obsolete("No code consumers in Core or any sub-mod. Scheduled for removal in a future version.")]
        public static IAgentActionBridge GetAgentActionBridge() => Ext.GetAgentActionBridge();
        public static void RegisterParameterTuner(IParameterTuner tuner) => Ext.RegisterParameterTuner(tuner);
        [Obsolete("No code consumers in Core or any sub-mod. Scheduled for removal in a future version.")]
        public static IReadOnlyList<IParameterTuner> ParameterTuners => Ext.ParameterTuners;

        public static Result<string?, RimMindError> GetProviderData(string category, Pawn pawn) => Providers.GetProviderData(category, pawn);
        public static Result<string?, RimMindError> GetStaticProviderData(string category) => Providers.GetStaticProviderData(category);
        public static List<string> GetRegisteredCategories() => Providers.GetRegisteredCategories();
        public static int UnregisterModProviders(string modId) => Providers.UnregisterByOwner(modId);

        public static bool IsConfigured() => Settings.IsConfigured();
        internal static IHistoryManager GetHistoryManager() => Settings.GetHistoryManager();
        public static IContextEngine GetContextEngine() => Settings.GetContextEngine();
        internal static IBudgetScheduler? GetContextScheduler() => Settings.GetContextScheduler();
        internal static EmbeddingSnapshotStore? GetEmbeddingSnapshotStore() => Settings.GetEmbeddingSnapshotStore();
        [Obsolete("No code consumers in Core or any sub-mod. Scheduled for removal in a future version.")]
        public static ITelemetryCollector Telemetry => Settings.Telemetry;

        [Obsolete("No code consumers in Core or any sub-mod. Scheduled for removal in a future version.")]
        public static IAudioPlayer AudioPlayer => Audio.AudioPlayer;

        [Obsolete("No code consumers in Core or any sub-mod. Scheduled for removal in a future version.")]
        public static IAgentBus GetAgentBus() => Bus.GetAgentBus();
        public static void PublishPerception(int pawnId, string type, string content, float importance = 0.5f) => Bus.PublishPerception(pawnId, type, content, importance);
        public static void RegisterPendingRequest(UIRequestEntry entry) => Bus.RegisterPendingRequest(entry);
        public static IReadOnlyList<UIRequestEntry> GetPendingRequests() => Bus.GetPendingRequests();
        public static bool DismissPendingRequest(UIRequestEntry entry) => Bus.DismissPendingRequest(entry);
        internal static IAIClient? GetClient() => Bus.GetClient();
        public static void InvalidateClientCache() => Bus.InvalidateClientCache();
        [Obsolete("No code consumers in Core or any sub-mod. Scheduled for removal in a future version.")]
        public static IAIClient? GetPlayer2Client() => Bus.GetPlayer2Client();

        public static string? GetNpcForMap(Map map)
            => GameServiceRef<INpcManager>.Optional().ValueOrDefault?.GetNpcForMap(map);

        public static bool IsAgentActive(string thingId)
            => RuntimeServiceRef<IAgentActiveChecker>.Optional().ValueOrDefault?.IsAgentActive(thingId) == true;

        /// <summary>
        /// Add a middleware to the corresponding pipeline based on TContext type.
        /// Supported context types: AIRequestContext, NpcChatContext, ContextBuildContext, BusPublishContext.
        /// </summary>
        public static void AddMiddleware<TContext>(IMiddleware<TContext> middleware) where TContext : IPipelineContext
            => CurrentRuntime.AddMiddleware(middleware);
    }
}
