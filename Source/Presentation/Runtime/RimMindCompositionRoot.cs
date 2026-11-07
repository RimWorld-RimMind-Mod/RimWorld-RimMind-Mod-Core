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
            private readonly Dictionary<Type, object?> _overrides = new Dictionary<Type, object?>();

            public CompositionResult()
            {
            }

            public CompositionResult(RuntimeServiceSnapshot services)
            {
                Services = services ?? throw new ArgumentNullException(nameof(services));
            }

            public RuntimeServiceSnapshot? Services { get; init; }

            private T ResolveRequired<T>() where T : class
            {
                if (_overrides.TryGetValue(typeof(T), out var overridden) && overridden is T typed)
                    return typed;
                if (Services == null)
                    throw new InvalidOperationException($"Cannot resolve required service '{typeof(T).FullName}' without a RuntimeServiceSnapshot.");
                return Services.GetRequired<T>();
            }

            private T? ResolveOptional<T>() where T : class
            {
                if (_overrides.TryGetValue(typeof(T), out var overridden))
                    return overridden as T;
                return Services?.Get<T>();
            }

            // Application layer services
            public IAgentBus AgentBus { get => ResolveRequired<IAgentBus>(); init => _overrides[typeof(IAgentBus)] = value; }
            public IToolRegistry ToolRegistry { get => ResolveRequired<IToolRegistry>(); init => _overrides[typeof(IToolRegistry)] = value; }
            public IRequestQueue Queue { get => ResolveRequired<IRequestQueue>(); init => _overrides[typeof(IRequestQueue)] = value; }
            public ITelemetryCollector Telemetry { get => ResolveRequired<ITelemetryCollector>(); init => _overrides[typeof(ITelemetryCollector)] = value; }
            public IFlywheelParameterStore ParameterStore { get => ResolveRequired<IFlywheelParameterStore>(); init => _overrides[typeof(IFlywheelParameterStore)] = value; }

            // Infrastructure layer services
            public IAudioPlayer AudioPlayer { get => ResolveRequired<IAudioPlayer>(); init => _overrides[typeof(IAudioPlayer)] = value; }
            public ITickProvider TickProvider { get => ResolveRequired<ITickProvider>(); init => _overrides[typeof(ITickProvider)] = value; }
            public IThreadChecker ThreadChecker { get => ResolveRequired<IThreadChecker>(); init => _overrides[typeof(IThreadChecker)] = value; }
            public ILogSink LogSink { get => ResolveRequired<ILogSink>(); init => _overrides[typeof(ILogSink)] = value; }
            public ITranslationService TranslationService { get => ResolveRequired<ITranslationService>(); init => _overrides[typeof(ITranslationService)] = value; }
            public IGameMechanismRegistry MechanismRegistry { get => ResolveRequired<IGameMechanismRegistry>(); init => _overrides[typeof(IGameMechanismRegistry)] = value; }
            public IWindowService? WindowService { get => ResolveOptional<IWindowService>(); init => _overrides[typeof(IWindowService)] = value; }
            public IPlayer2Lifecycle Player2Lifecycle { get => ResolveRequired<IPlayer2Lifecycle>(); init => _overrides[typeof(IPlayer2Lifecycle)] = value; }

            // Presentation layer services
            public IProviderRegistry ProviderRegistry { get => ResolveRequired<IProviderRegistry>(); init => _overrides[typeof(IProviderRegistry)] = value; }
            public IHistoryManager HistoryManager { get => ResolveRequired<IHistoryManager>(); init => _overrides[typeof(IHistoryManager)] = value; }
            public IClientManager ClientManager { get => ResolveRequired<IClientManager>(); init => _overrides[typeof(IClientManager)] = value; }
            public ISensorManager SensorManager { get => ResolveRequired<ISensorManager>(); init => _overrides[typeof(ISensorManager)] = value; }
            public IOverlayService OverlayService { get => ResolveRequired<IOverlayService>(); init => _overrides[typeof(IOverlayService)] = value; }
            public IContextEngine ContextEngine { get => ResolveRequired<IContextEngine>(); init => _overrides[typeof(IContextEngine)] = value; }
            public IPawnAgentFactoryVerse PawnAgentFactory { get => ResolveRequired<IPawnAgentFactoryVerse>(); init => _overrides[typeof(IPawnAgentFactoryVerse)] = value; }
            public IGameContextBuilder GameContextBuilder { get => ResolveRequired<IGameContextBuilder>(); init => _overrides[typeof(IGameContextBuilder)] = value; }
            public IResponseDispatcher ResponseDispatcher { get => ResolveRequired<IResponseDispatcher>(); init => _overrides[typeof(IResponseDispatcher)] = value; }
            public IContextKeyRegistry ContextKeyRegistry { get => ResolveRequired<IContextKeyRegistry>(); init => _overrides[typeof(IContextKeyRegistry)] = value; }
            public IContextKeyProvider ContextKeyProvider { get => ResolveRequired<IContextKeyProvider>(); init => _overrides[typeof(IContextKeyProvider)] = value; }
            public IContextCacheManager CacheManager { get => ResolveRequired<IContextCacheManager>(); init => _overrides[typeof(IContextCacheManager)] = value; }
            public IContextDiffTracker DiffTracker { get => ResolveRequired<IContextDiffTracker>(); init => _overrides[typeof(IContextDiffTracker)] = value; }
            public IContextLayerBuilder LayerBuilder { get => ResolveRequired<IContextLayerBuilder>(); init => _overrides[typeof(IContextLayerBuilder)] = value; }
            public IBudgetScheduler BudgetScheduler { get => ResolveRequired<IBudgetScheduler>(); init => _overrides[typeof(IBudgetScheduler)] = value; }
            public IRelevanceTable RelevanceTable { get => ResolveRequired<IRelevanceTable>(); init => _overrides[typeof(IRelevanceTable)] = value; }
            public IRelevanceLearner RelevanceLearner { get => ResolveRequired<IRelevanceLearner>(); init => _overrides[typeof(IRelevanceLearner)] = value; }

            // Pipelines
            public IPipeline<BusPublishContext> BusPublishPipeline { get => ResolveRequired<IPipeline<BusPublishContext>>(); init => _overrides[typeof(IPipeline<BusPublishContext>)] = value; }
            public IPipeline<LlmRequestContext> UnifiedPipeline { get => ResolveRequired<IPipeline<LlmRequestContext>>(); init => _overrides[typeof(IPipeline<LlmRequestContext>)] = value; }

            public INpcManagerAccessor NpcManagers { get => ResolveRequired<INpcManagerAccessor>(); init => _overrides[typeof(INpcManagerAccessor)] = value; }
            public IAIDebugLogAccessor AIDebugLogs { get => ResolveRequired<IAIDebugLogAccessor>(); init => _overrides[typeof(IAIDebugLogAccessor)] = value; }

            // Settings
            public ISettingsProvider SettingsProvider { get => ResolveRequired<ISettingsProvider>(); init => _overrides[typeof(ISettingsProvider)] = value; }
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
                (agentBus as Application.Features.AgentBus.AgentBusImpl)?.SetLogSink(logSink);
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
                var lifecycleCleanupKey = agentBus.Subscribe<Domain.Events.AgentLifecycleEvent>(evt =>
                {
                    if (!string.IsNullOrEmpty(evt.NpcId) &&
                        (string.Equals(evt.NewState, "Dead", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(evt.NewState, "Removed", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(evt.NewState, "Despawned", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(evt.NewState, "Terminated", StringComparison.OrdinalIgnoreCase)))
                    {
                        contextServices.ContextEngine.InvalidateNpc(evt.NpcId);
                    }
                });
                owned.Add(new ActionLease(() => agentBus.Unsubscribe<Domain.Events.AgentLifecycleEvent>(lifecycleCleanupKey)));

                var busMiddleware = extensions.GetExtensionRegistry<IMiddleware<BusPublishContext>>();
                services.Bind(busMiddleware);
                var busPublishPipeline = BusPublishPipelineFactory.Build(
                    agentBus.DispatchAction ?? (evt => agentBus.DispatchAction?.Invoke(evt)),
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
                    services, infraBag.MechanismRegistry, appBag.ToolRegistry);
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
                    pawn => runtime?.GetAgentIdentity(pawn),
                    existingGameContextBuilder: contextServices.GameContextBuilder);

                SettingsComposition.ComposeDefaultExtensionRegistries(services, extensions);
                appBag.Queue.ModCooldowns = extensions.GetExtensionRegistry<IModCooldown>();
                ClientComposition.RegisterBuiltinClientFactories(
                    clientServices.ClientFactoryRegistry,
                    logSink,
                    openAISettings ?? resolvedSettings as IOpenAISettings);
                var remoteSync = ClientComposition.ComposeRemoteSync(services, logSink);
                UiComposition.RegisterRemoteSyncSettingsTab(
                    extensions.GetExtensionRegistry<ISettingsTab>(),
                    remoteSync.Settings,
                    remoteSync.Service);

                var result = new CompositionResult(services.Build());

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
