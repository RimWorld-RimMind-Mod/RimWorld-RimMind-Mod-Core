using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Interfaces.Agent.Modes;
using RimMind.Application.Common.Interfaces.Client;
using RimMind.Application.Common.Interfaces.Context;
using RimMind.Application.Common.Interfaces.Extension;
using RimMind.Application.Common.Interfaces.Flywheel;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Interfaces.Mechanisms;
using RimMind.Application.Common.Interfaces.Npc;
using RimMind.Application.Common.Interfaces.Pipeline;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Application.Common.Models.Context;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;
using RimMind.Presentation.Runtime;
using RimMind.Presentation.Runtime.Services;
using Verse;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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

        internal static void ResetForNewGame() => RimMindRuntime.ResetInstance();

        // === Unified Request API ===
        public static void Send(LlmRequestEnvelope envelope, Action<Result<LlmResponse, RimMindError>> onComplete)
            => Request.Send(envelope, onComplete);
        public static Task<Result<LlmResponse, RimMindError>> SendAsync(LlmRequestEnvelope envelope)
            => Request.SendAsync(envelope);

        public static IToolRegistry Tools => ToolSet.Registry;
        public static IGameMechanismRegistry Mechanisms => ToolSet.Mechanisms;
        public static IExtensionRegistry<IAgentMode> Modes => Ext.Get<IAgentMode>();
        public static IExtensionRegistry<IModeTransitionPolicy> ModePolicies => Ext.Get<IModeTransitionPolicy>();

        public static IExtensionRegistry<T> Extensions<T>() where T : class, IExtension => Ext.Get<T>();
        public static void RegisterParameterTuner(IParameterTuner tuner) => Ext.RegisterParameterTuner(tuner);

        internal static IHistoryManager GetHistoryManager() => Settings.GetHistoryManager();
        internal static IBudgetScheduler? GetContextScheduler() => Settings.GetContextScheduler();
        internal static EmbeddingSnapshotStore? GetEmbeddingSnapshotStore() => Settings.GetEmbeddingSnapshotStore();

        public static void RegisterPendingRequest(UIRequestEntry entry) => Bus.RegisterPendingRequest(entry);
        public static IReadOnlyList<UIRequestEntry> GetPendingRequests() => Bus.GetPendingRequests();
        public static bool DismissPendingRequest(UIRequestEntry entry) => Bus.DismissPendingRequest(entry);
        internal static IAIClient? GetClient() => Bus.GetClient();

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
