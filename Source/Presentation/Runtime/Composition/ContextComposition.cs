using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Context;
using RimMind.Application.Common.Interfaces.Flywheel;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Interfaces.Npc;
using RimMind.Application.Common.Models.Context;
using RimMind.Application.Features.Agent.InnerVoice;
using RimMind.Application.Features.Context;
using RimMind.Domain.Interfaces;
using RimMind.Presentation.Context;
using RimMind.Presentation.Runtime.Services;

namespace RimMind.Presentation.Runtime.Composition
{
    internal sealed class ContextCompositionServices
    {
        public IProviderRegistry ProviderRegistry { get; init; } = null!;
        public IHistoryManager HistoryManager { get; init; } = null!;
        public IContextEngine ContextEngine { get; init; } = null!;
        public IContextKeyRegistry ContextKeyRegistry { get; init; } = null!;
        public IContextKeyProvider ContextKeyProvider { get; init; } = null!;
        public IContextCacheManager CacheManager { get; init; } = null!;
        public IContextDiffTracker DiffTracker { get; init; } = null!;
        public IContextLayerBuilder LayerBuilder { get; init; } = null!;
        public IBudgetScheduler BudgetScheduler { get; init; } = null!;
        public IRelevanceTable RelevanceTable { get; init; } = null!;
        public IRelevanceLearner RelevanceLearner { get; init; } = null!;
        public InnerVoiceHandler InnerVoiceHandler { get; init; } = null!;
    }

    internal static class ContextComposition
    {
        public static ContextCompositionServices Compose(
            RuntimeServiceBuilder services,
            ISettingsProvider resolvedSettings,
            IAgentBus agentBus,
            ITickProvider tickProvider,
            ILogSink logSink,
            INpcManagerAccessor npcManagers,
            ITranslationService translationService,
            IFlywheelParameterStore flywheelParameterStore,
            IEmbedCache embedCache)
        {
            var providerRegistry = new ProviderRegistry();
            services.Bind<IProviderRegistry>(providerRegistry);

            var historyManager = new HistoryManager(tickProvider);
            services.Bind<IHistoryManager>(historyManager);

            var innerVoiceHandler = new InnerVoiceHandler(agentBus, tickProvider, logSink);
            innerVoiceHandler.StartListening();
            services.Bind(innerVoiceHandler);

            var cacheManager = new ContextCacheManager(logSink, embedCache);
            var diffTracker = new ContextDiffTracker(logSink);
            var keyProvider = new DefaultContextKeyProvider();
            var layerBuilder = new ContextLayerBuilder();
            var providerCache = new ProviderCache(agentBus, logSink, tickProvider);
            var keyRegistryImpl = new ContextKeyRegistryImpl(logSink, providerCache);
            CoreContextProviders.RegisterAll(
                keyRegistryImpl,
                translationService,
                keyProvider,
                npcManagers);
            var relevanceTableImpl = new RelevanceTableImpl();
            var relevanceLearner = new RelevanceLearner(tickProvider);
            var budgetScheduler = new BudgetScheduler(relevanceTableImpl, relevanceLearner, tickProvider, cacheManager.EmbedCache);
            var schemaRegistry = new SchemaRegistry(logSink);
            var buildServices = new ContextBuildServices(cacheManager, diffTracker, layerBuilder, budgetScheduler);

            services.Bind<IBudgetScheduler>(budgetScheduler);
            services.Bind<IContextCacheManager>(cacheManager);
            services.Bind<IContextDiffTracker>(diffTracker);
            services.Bind<IContextLayerBuilder>(layerBuilder);
            services.Bind<IContextKeyRegistry>(keyRegistryImpl);
            services.Bind<IRelevanceTable>(relevanceTableImpl);
            services.Bind<IRelevanceLearner>(relevanceLearner);
            services.Bind(schemaRegistry);

            var embeddingSnapshotStore = new EmbeddingSnapshotStore();
            var contextEngine = new ContextOrchestrator(
                historyManager,
                npcManagers,
                buildServices,
                resolvedSettings,
                translationService,
                flywheelParameterStore,
                logSink,
                embeddingSnapshotStore,
                keyRegistryImpl,
                relevanceTableImpl,
                providerCache,
                tickProvider);

            services.Bind<IContextEngine>(contextEngine);
            services.Bind<IContextKeyProvider>(keyProvider);
            services.Bind<IContextBuilder>(contextEngine);
            services.Bind<IContextCache>(contextEngine);

            return new ContextCompositionServices
            {
                ProviderRegistry = providerRegistry,
                HistoryManager = historyManager,
                ContextEngine = contextEngine,
                ContextKeyRegistry = keyRegistryImpl,
                ContextKeyProvider = keyProvider,
                CacheManager = cacheManager,
                DiffTracker = diffTracker,
                LayerBuilder = layerBuilder,
                BudgetScheduler = budgetScheduler,
                RelevanceTable = relevanceTableImpl,
                RelevanceLearner = relevanceLearner,
                InnerVoiceHandler = innerVoiceHandler
            };
        }
    }
}
