using System;
using System.Collections.Generic;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Interfaces.Async;
using RimMind.Application.Common.Interfaces.Client;
using RimMind.Application.Common.Interfaces.Context;
using RimMind.Application.Common.Interfaces.Diagnostics;
using RimMind.Application.Common.Interfaces.Extension;
using RimMind.Application.Common.Interfaces.Flywheel;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Interfaces.Mechanisms;
using RimMind.Application.Common.Interfaces.Npc;
using RimMind.Application.Common.Interfaces.Pipeline;
using RimMind.Application.Common.Interfaces.Sensor;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Application.Common.Interfaces.UI;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Application.Common.Interfaces.Agent.Psychology;
using RimMind.Application.Features.Context;
using RimMind.Application.Features.Pipeline.Bus;
using RimMind.Application.Features.Pipeline.Unified;
using RimMind.Application.Features.Requests;
using RimMind.Application.Features.Requests.Queue;
using RimMind.Infrastructure;
using RimMind.Infrastructure.Cache;
using RimMind.Presentation.Agent;
using RimMind.Presentation.Settings;
using RimMind.Presentation.Runtime.Composition;
using RimMind.Presentation.Runtime.Services;
using RimMind.Application.Common.Interfaces.Runtime;

namespace RimMind.Presentation.Runtime
{
    /// <summary>
    /// Composition Root: responsible for all service instantiation and DI registration.
    /// Extracted from RimMindRuntime to satisfy SRP.
    /// </summary>
    internal sealed class RimMindCompositionRoot
    {
        public sealed class CompositionResult
        {
            // Application layer services
            public IAgentBus AgentBus { get; init; } = null!;
            public IToolRegistry ToolRegistry { get; init; } = null!;
            public IRequestQueue Queue { get; init; } = null!;
            public ITelemetryCollector Telemetry { get; init; } = null!;
            public IFlywheelParameterStore ParameterStore { get; init; } = null!;

            // Infrastructure layer services
            public IAudioPlayer AudioPlayer { get; init; } = null!;
            public ITickProvider TickProvider { get; init; } = null!;
            public IThreadChecker ThreadChecker { get; init; } = null!;
            public ILogSink LogSink { get; init; } = null!;
            public ITranslationService TranslationService { get; init; } = null!;
            public IGameMechanismRegistry MechanismRegistry { get; init; } = null!;
            public IWindowService? WindowService { get; init; }
            public IPlayer2Lifecycle Player2Lifecycle { get; init; } = null!;

            // Presentation layer services
            public IProviderRegistry ProviderRegistry { get; init; } = null!;
            public IHistoryManager HistoryManager { get; init; } = null!;
            public IClientManager ClientManager { get; init; } = null!;
            public ISensorManager SensorManager { get; init; } = null!;
            public IOverlayService OverlayService { get; init; } = null!;
            public IContextEngine ContextEngine { get; init; } = null!;
            public IPawnAgentFactoryVerse PawnAgentFactory { get; init; } = null!;
            public IGameContextBuilder GameContextBuilder { get; init; } = null!;
            public IResponseDispatcher ResponseDispatcher { get; init; } = null!;
            public IContextKeyRegistry ContextKeyRegistry { get; init; } = null!;
            public IContextKeyProvider ContextKeyProvider { get; init; } = null!;
            public IContextCacheManager CacheManager { get; init; } = null!;
            public IContextDiffTracker DiffTracker { get; init; } = null!;
            public IContextLayerBuilder LayerBuilder { get; init; } = null!;
            public IBudgetScheduler BudgetScheduler { get; init; } = null!;
            public IRelevanceTable RelevanceTable { get; init; } = null!;
            public IRelevanceLearner RelevanceLearner { get; init; } = null!;

            // Pipelines
            public IPipeline<BusPublishContext> BusPublishPipeline { get; init; } = null!;
            public IPipeline<LlmRequestContext> UnifiedPipeline { get; init; } = null!;

            public INpcManagerAccessor NpcManagers { get; init; } = null!;
            public IAIDebugLogAccessor AIDebugLogs { get; init; } = null!;

            // Settings
            public ISettingsProvider SettingsProvider { get; init; } = null!;
        }

        public RuntimeComposition Compose(
            Guid runtimeId,
            ISettingsProvider? settingsProvider,
            IOpenAISettings? openAISettings,
            ExtensionRegistryCatalog extensions,
            AgentActionBridgeSlot actionBridge)
        {
            if (extensions == null) throw new ArgumentNullException(nameof(extensions));
            if (actionBridge == null) throw new ArgumentNullException(nameof(actionBridge));

            var services = new RuntimeServiceBuilder(runtimeId);
            var lifetime = new RuntimeLifetime(
                services.RuntimeId,
                RuntimeServiceHub.Shared.IsCurrent,
                () => RuntimeServiceHub.Shared.RecordStaleCompletion(
                    LifecycleEventSources.RuntimeLifetime));
            var owned = new List<IDisposable>();
            RimMindRuntime? runtime = null;

            try
            {
                var resolvedSettings = settingsProvider
                    ?? throw new ArgumentNullException(nameof(settingsProvider));
                SettingsComposition.Compose(services, resolvedSettings, openAISettings);

                var appBag = Application.DependencyInjection.AddApplicationServices(resolvedSettings, lifetime);
                SettingsComposition.ComposeApplicationServices(services, appBag);
                var infraBag = Infrastructure.DependencyInjection.AddInfrastructureServices(
                    appBag.ToolRegistry, appBag.JsonExtractor, resolvedSettings);
                SettingsComposition.ComposeInfrastructureServices(services, infraBag);

                var logSink = infraBag.LogSink;
                var tickProvider = infraBag.TickProvider;
                var agentBus = appBag.AgentBus;
                var flywheelParameterStore = appBag.ParameterStore;
                var npcManagers = new NpcManagerAccessor();
                var aiDebugLogs = new AIDebugLogAccessor();
                services.Bind<INpcManagerAccessor>(npcManagers);
                services.Bind<IAIDebugLogAccessor>(aiDebugLogs);
                services.Bind<ICompletionFence>(lifetime);
                services.Bind<IAgentActionBridgeAccessor>(actionBridge);

                ToolMechanismComposition.RegisterAllMechanisms(infraBag.MechanismRegistry);

                var clientServices = ClientComposition.ComposeClientManager(
                    services, extensions, resolvedSettings);
                var uiServices = UiComposition.ComposeServices(services, extensions);
                var contextServices = ContextComposition.Compose(
                    services,
                    resolvedSettings,
                    agentBus,
                    tickProvider,
                    logSink,
                    npcManagers,
                    infraBag.TranslationService,
                    flywheelParameterStore,
                    new EmbedCache());
                owned.Add(new ActionLease(contextServices.InnerVoiceHandler.StopListening));

                var busMiddleware = extensions.GetExtensionRegistry<IMiddleware<BusPublishContext>>();
                services.Bind(busMiddleware);
                var busPublishPipeline = BusPublishPipelineFactory.Build(
                    evt => agentBus.DispatchAction?.Invoke(evt),
                    logSink,
                    infraBag.ThreadChecker,
                    busMiddleware);
                services.Bind<IPipeline<BusPublishContext>>(busPublishPipeline);
                agentBus.SetPipeline(busPublishPipeline);

                var llmMiddleware = extensions.GetExtensionRegistry<IMiddleware<LlmRequestContext>>();
                services.Bind(llmMiddleware);
                var unifiedPipeline = UnifiedRequestPipelineFactory.Build(
                    appBag.ToolRegistry,
                    logSink,
                    npcManagers,
                    contextServices.ContextEngine,
                    appBag.Telemetry,
                    resolvedSettings,
                    llmMiddleware,
                    contextServices.RelevanceLearner,
                    new AIResponseAnalyzer(),
                    infraBag.RequestTraceLog);
                services.Bind<IPipeline<LlmRequestContext>>(unifiedPipeline);
                var requestSubmission = new RequestSubmissionService(
                    appBag.Queue,
                    clientServices.ClientManager,
                    unifiedPipeline,
                    infraBag.RequestTraceLog,
                    resolvedSettings,
                    lifetime);
                services.Bind<IRequestSubmissionService>(requestSubmission);

                var actionExecutor = ToolMechanismComposition.ComposeActionExecutor(
                    services, infraBag.MechanismRegistry);
                var agentServices = AgentComposition.ComposeAgents(
                    services,
                    extensions,
                    resolvedSettings,
                    agentBus,
                    actionExecutor,
                    contextServices.InnerVoiceHandler,
                    null,
                    tickProvider,
                    logSink,
                    npcManagers,
                    lifetime,
                    pawn => runtime?.GetAgentIdentity(pawn));

                SettingsComposition.ComposeDefaultExtensionRegistries(services, extensions);
                ClientComposition.RegisterBuiltinClientFactories(
                    clientServices.ClientFactoryRegistry,
                    logSink,
                    openAISettings ?? resolvedSettings as IOpenAISettings);
                var remoteSync = ClientComposition.ComposeRemoteSync(services, logSink);
                UiComposition.RegisterRemoteSyncSettingsTab(
                    extensions.GetExtensionRegistry<ISettingsTab>(),
                    remoteSync.Settings,
                    remoteSync.Service);

                var result = new CompositionResult
                {
                    AgentBus = agentBus,
                    ToolRegistry = appBag.ToolRegistry,
                    Queue = appBag.Queue,
                    Telemetry = appBag.Telemetry,
                    ParameterStore = flywheelParameterStore,
                    AudioPlayer = infraBag.AudioPlayer,
                    TickProvider = tickProvider,
                    ThreadChecker = infraBag.ThreadChecker,
                    LogSink = logSink,
                    TranslationService = infraBag.TranslationService,
                    MechanismRegistry = infraBag.MechanismRegistry,
                    WindowService = infraBag.WindowService,
                    Player2Lifecycle = infraBag.Player2Lifecycle,
                    ProviderRegistry = contextServices.ProviderRegistry,
                    HistoryManager = contextServices.HistoryManager,
                    ClientManager = clientServices.ClientManager,
                    SensorManager = uiServices.SensorManager,
                    OverlayService = uiServices.OverlayService,
                    ContextEngine = contextServices.ContextEngine,
                    PawnAgentFactory = agentServices.PawnAgentFactory,
                    GameContextBuilder = agentServices.GameContextBuilder,
                    ResponseDispatcher = agentServices.ResponseDispatcher,
                    ContextKeyRegistry = contextServices.ContextKeyRegistry,
                    ContextKeyProvider = contextServices.ContextKeyProvider,
                    CacheManager = contextServices.CacheManager,
                    DiffTracker = contextServices.DiffTracker,
                    LayerBuilder = contextServices.LayerBuilder,
                    BudgetScheduler = contextServices.BudgetScheduler,
                    RelevanceTable = contextServices.RelevanceTable,
                    RelevanceLearner = contextServices.RelevanceLearner,
                    BusPublishPipeline = busPublishPipeline,
                    UnifiedPipeline = unifiedPipeline,
                    NpcManagers = npcManagers,
                    AIDebugLogs = aiDebugLogs,
                    SettingsProvider = resolvedSettings
                };

                var lifecycle = new RimMindLifecycleManager(
                    result.Telemetry,
                    result.ContextEngine,
                    result.Player2Lifecycle,
                    result.AgentBus,
                    result.ContextKeyRegistry);
                var extensionManager = new RimMindExtensionManager(
                    result.LogSink,
                    result.TickProvider,
                    result.AgentBus,
                    actionBridge);
                runtime = new RimMindRuntime(result, lifecycle, extensionManager, extensions);
                var modeRegistry = extensions.GetExtensionRegistry<Application.Common.Interfaces.Agent.Modes.IAgentMode>();
                extensionManager.RegisterBuiltinModes(modeRegistry);
                services.Bind(modeRegistry);
                services.Bind(runtime);
                services.Bind<IRimMindRuntime>(runtime);
                services.Bind(extensions);

                services
                    .Require<RimMindRuntime>()
                    .Require<IRimMindRuntime>()
                    .Require<IAgentBus>()
                    .Require<IRequestQueue>()
                    .Require<IPipeline<LlmRequestContext>>()
                    .Require<ICompletionFence>()
                    .Require<IAgentActionBridgeAccessor>();
                services.Build();
                return new RuntimeComposition(runtime, services, extensions, lifetime, owned);
            }
            catch
            {
                runtime?.Shutdown();
                for (var index = owned.Count - 1; index >= 0; index--)
                    owned[index].Dispose();
                lifetime.Dispose();
                throw;
            }
        }

    }
}
