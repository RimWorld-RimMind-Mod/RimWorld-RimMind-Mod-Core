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
        public const int ApiVersion = 2;

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
        [Obsolete("Use RimMindAPI.Request.PauseQueue instead.")]
        public static void PauseQueue() => Request.PauseQueue();
        [Obsolete("Use RimMindAPI.Request.ResumeQueue instead.")]
        public static void ResumeQueue() => Request.ResumeQueue();
        [Obsolete("Use RimMindAPI.Request.ActiveRequestCount instead.")]
        public static int ActiveRequestCount => Request.ActiveRequestCount;
        [Obsolete("No code consumers in Core or any sub-mod. Scheduled for removal in a future version.")]
        public static IReadOnlyList<ClientTrackedRequest> GetActiveRequests() => Request.GetActiveRequests();
        [Obsolete("No code consumers in Core or any sub-mod. Scheduled for removal in a future version.")]
        public static IReadOnlyList<ClientTrackedRequest> GetAllQueuedRequests() => Request.GetAllQueuedRequests();
        [Obsolete("Use RimMindAPI.Request.TotalQueuedCount instead.")]
        public static int TotalQueuedCount => Request.TotalQueuedCount;
        [Obsolete("Use RimMindAPI.Request.ClearModCooldown instead.")]
        public static void ClearModCooldown(string modId) => Request.ClearModCooldown(modId);
        [Obsolete("Use RimMindAPI.Request.GetModCooldownTicksLeft instead.")]
        public static int GetModCooldownTicksLeft(string modId) => Request.GetModCooldownTicksLeft(modId);

        [Obsolete("No code consumers in Core or any sub-mod. Scheduled for removal in a future version.")]
        public static string BuildMapContext(Map map, bool brief = false) => ChatFlow.BuildMapContext(map, brief);

        public static IToolRegistry Tools => ToolSet.Registry;
        public static IGameMechanismRegistry Mechanisms => ToolSet.Mechanisms;
        public static IExtensionRegistry<IAgentMode> Modes => Ext.Get<IAgentMode>();
        public static IExtensionRegistry<IModeTransitionPolicy> ModePolicies => Ext.Get<IModeTransitionPolicy>();

        public static IExtensionRegistry<T> Extensions<T>() where T : class, IExtension => Ext.Get<T>();
        [Obsolete("Use RimMindAPI.Ext.ShouldSkipDialogue instead.")]
        public static bool ShouldSkipDialogue(Pawn pawn, string trigger) => Ext.ShouldSkipDialogue(pawn, trigger);
        [Obsolete("Use RimMindAPI.Ext.ShouldSkipFloatMenu instead.")]
        public static bool ShouldSkipFloatMenu() => Ext.ShouldSkipFloatMenu();
        [Obsolete("Use RimMindAPI.Ext.ShouldSkipAction instead.")]
        public static bool ShouldSkipAction(string intentId) => Ext.ShouldSkipAction(intentId);
        [Obsolete("Use RimMindAPI.Ext.ShouldSkipStorytellerIncident instead.")]
        public static bool ShouldSkipStorytellerIncident() => Ext.ShouldSkipStorytellerIncident();
        [Obsolete("Use RimMindAPI.Ext.TriggerDialogue instead.")]
        public static void TriggerDialogue(Pawn pawn, string context, Pawn? recipient = null) => Ext.TriggerDialogue(pawn, context, recipient);
        [Obsolete("Use RimMindAPI.Ext.NotifyIncidentExecuted instead.")]
        public static void NotifyIncidentExecuted() => Ext.NotifyIncidentExecuted();
        [Obsolete("Use RimMindAPI.Ext.CanTriggerDialogue instead.")]
        public static bool CanTriggerDialogue => Ext.CanTriggerDialogue;
        [Obsolete("Use RimMindAPI.Ext.RegisterAgentIdentityProvider instead.")]
        public static void RegisterAgentIdentityProvider(Func<Pawn, AgentIdentity?> provider) => Ext.RegisterAgentIdentityProvider(provider);
        [Obsolete("No code consumers in Core or any sub-mod. Scheduled for removal in a future version.")]
        public static AgentIdentity? GetAgentIdentity(Pawn pawn) => Ext.GetAgentIdentity(pawn);
        [Obsolete("Use RimMindAPI.Ext.RegisterAgentActionBridge instead.")]
        public static void RegisterAgentActionBridge(IAgentActionBridge bridge) => Ext.RegisterAgentActionBridge(bridge);
        [Obsolete("No code consumers in Core or any sub-mod. Scheduled for removal in a future version.")]
        public static IAgentActionBridge GetAgentActionBridge() => Ext.GetAgentActionBridge();
        public static void RegisterParameterTuner(IParameterTuner tuner) => Ext.RegisterParameterTuner(tuner);
        [Obsolete("No code consumers in Core or any sub-mod. Scheduled for removal in a future version.")]
        public static IReadOnlyList<IParameterTuner> ParameterTuners => Ext.ParameterTuners;

        [Obsolete("Use RimMindAPI.Providers.GetProviderData instead.")]
        public static Result<string?, RimMindError> GetProviderData(string category, Pawn pawn) => Providers.GetProviderData(category, pawn);
        [Obsolete("Use RimMindAPI.Providers.GetStaticProviderData instead.")]
        public static Result<string?, RimMindError> GetStaticProviderData(string category) => Providers.GetStaticProviderData(category);
        [Obsolete("Use RimMindAPI.Providers.GetRegisteredCategories instead.")]
        public static List<string> GetRegisteredCategories() => Providers.GetRegisteredCategories();
        [Obsolete("Use RimMindAPI.Providers.UnregisterByOwner instead.")]
        public static int UnregisterModProviders(string modId) => Providers.UnregisterByOwner(modId);

        [Obsolete("Use RimMindAPI.Settings.IsConfigured instead.")]
        public static bool IsConfigured() => Settings.IsConfigured();
        internal static IHistoryManager GetHistoryManager() => Settings.GetHistoryManager();
        [Obsolete("Use RimMindAPI.Settings.GetContextEngine instead.")]
        public static IContextEngine GetContextEngine() => Settings.GetContextEngine();
        internal static IBudgetScheduler? GetContextScheduler() => Settings.GetContextScheduler();
        internal static EmbeddingSnapshotStore? GetEmbeddingSnapshotStore() => Settings.GetEmbeddingSnapshotStore();
        [Obsolete("No code consumers in Core or any sub-mod. Scheduled for removal in a future version.")]
        public static ITelemetryCollector Telemetry => Settings.Telemetry;

        [Obsolete("No code consumers in Core or any sub-mod. Scheduled for removal in a future version.")]
        public static IAudioPlayer AudioPlayer => Audio.AudioPlayer;

        [Obsolete("No code consumers in Core or any sub-mod. Scheduled for removal in a future version.")]
        public static IAgentBus GetAgentBus() => Bus.GetAgentBus();
        [Obsolete("Use RimMindAPI.Bus.PublishPerception instead.")]
        public static void PublishPerception(int pawnId, string type, string content, float importance = 0.5f) => Bus.PublishPerception(pawnId, type, content, importance);
        public static void RegisterPendingRequest(UIRequestEntry entry) => Bus.RegisterPendingRequest(entry);
        public static IReadOnlyList<UIRequestEntry> GetPendingRequests() => Bus.GetPendingRequests();
        public static bool DismissPendingRequest(UIRequestEntry entry) => Bus.DismissPendingRequest(entry);
        internal static IAIClient? GetClient() => Bus.GetClient();
        [Obsolete("Use RimMindAPI.Bus.InvalidateClientCache instead.")]
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
