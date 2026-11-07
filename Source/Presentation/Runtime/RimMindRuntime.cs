using System;
using System.Collections.Generic;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Client;
using RimMind.Application.Common.Interfaces.Context;
using RimMind.Application.Common.Interfaces.Extension;
using RimMind.Application.Common.Interfaces.Flywheel;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Interfaces.Mechanisms;
using RimMind.Application.Common.Interfaces.Pipeline;
using RimMind.Application.Common.Interfaces.Runtime;
using RimMind.Application.Common.Interfaces.Sensor;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Application.Common.Interfaces.UI;
using RimMind.Application.Common.Interfaces.Agent.Modes;
using RimMind.Application.Common.Models.Agent;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Application.Features.Registry;
using RimMind.Application.Features.Requests.Queue;
using RimMind.Presentation.Runtime.Composition;
using RimMind.Presentation.Runtime.Services;
using Verse;

namespace RimMind.Presentation.Runtime
{
    /// <summary>
    /// Thin facade for the RimMind runtime. Delegates to:
    /// - RimMindCompositionRoot: service instantiation and DI registration
    /// - RimMindLifecycleManager: Initialize/Shutdown lifecycle
    /// - RimMindExtensionManager: extension registration, middleware, agent identity
    /// </summary>
    public sealed class RimMindRuntime : IRimMindRuntime
    {
        // Sub-managers (SRP decomposition)
        private readonly RuntimeServiceSnapshot? _services;
        private readonly RimMindCompositionRoot.CompositionResult? _composition;
        private readonly RimMindLifecycleManager _lifecycleManager;
        private readonly RimMindExtensionManager _extensionManager;
        private readonly ExtensionRegistryCatalog _extensions;

        public RuntimeServiceSnapshot? Services => _services ?? _composition?.Services;

        private T ResolveRequired<T>(Func<RimMindCompositionRoot.CompositionResult, T> fallback)
            where T : class
        {
            if (_services != null)
                return _services.GetRequired<T>();
            if (_composition != null)
                return fallback(_composition);
            throw new InvalidOperationException($"Cannot resolve required service '{typeof(T).FullName}'.");
        }

        private T? ResolveOptional<T>(Func<RimMindCompositionRoot.CompositionResult, T?> fallback)
            where T : class
        {
            if (_services != null)
                return _services.Get<T>();
            return _composition != null ? fallback(_composition) : null;
        }

        // Public properties — resolve directly from RuntimeServiceSnapshot (with CompositionResult override fallback)
        public IAgentBus AgentBus => ResolveRequired<IAgentBus>(c => c.AgentBus);
        public IContextEngine ContextEngine => ResolveRequired<IContextEngine>(c => c.ContextEngine);
        public IHistoryManager HistoryManager => ResolveRequired<IHistoryManager>(c => c.HistoryManager);
        public IClientManager ClientManager => ResolveRequired<IClientManager>(c => c.ClientManager);
        public IAudioPlayer AudioPlayer => ResolveRequired<IAudioPlayer>(c => c.AudioPlayer);
        public IProviderRegistry ProviderRegistry => ResolveRequired<IProviderRegistry>(c => c.ProviderRegistry);
        public IOverlayService OverlayService => ResolveRequired<IOverlayService>(c => c.OverlayService);
        public IRequestQueue Queue => ResolveRequired<IRequestQueue>(c => c.Queue);
        public ITelemetryCollector Telemetry => ResolveRequired<ITelemetryCollector>(c => c.Telemetry);
        public IToolRegistry ToolRegistry => ResolveRequired<IToolRegistry>(c => c.ToolRegistry);
        public IGameMechanismRegistry MechanismRegistry => ResolveRequired<IGameMechanismRegistry>(c => c.MechanismRegistry);
        public IWindowService? WindowService => ResolveOptional<IWindowService>(c => c.WindowService);
        public IContextKeyRegistry ContextKeys => ResolveRequired<IContextKeyRegistry>(c => c.ContextKeyRegistry);
        public IRelevanceTable RelevanceTable => ResolveRequired<IRelevanceTable>(c => c.RelevanceTable);
        public IRelevanceLearner ContextLearner => ResolveRequired<IRelevanceLearner>(c => c.RelevanceLearner);
        public IGameContextBuilder GameContextBuilder => ResolveRequired<IGameContextBuilder>(c => c.GameContextBuilder);

        public IPipeline<BusPublishContext> BusPublishPipeline => ResolveRequired<IPipeline<BusPublishContext>>(c => c.BusPublishPipeline);
        public IPipeline<LlmRequestContext> UnifiedPipeline => ResolveRequired<IPipeline<LlmRequestContext>>(c => c.UnifiedPipeline);

        public IReadOnlyList<IParameterTuner> ParameterTunersList => _extensionManager.ParameterTuners;
        public Func<Pawn, AgentIdentity?>? AgentIdentityProvider => _extensionManager.AgentIdentityProvider;
        public IAgentActionBridge AgentActionBridge => _extensionManager.AgentActionBridge;
        public bool IsShutdown => _lifecycleManager.IsShutdown;

        internal RimMindRuntime(
            RuntimeServiceSnapshot services,
            RimMindLifecycleManager lifecycleManager,
            RimMindExtensionManager extensionManager,
            ExtensionRegistryCatalog extensions)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _lifecycleManager = lifecycleManager ?? throw new ArgumentNullException(nameof(lifecycleManager));
            _extensionManager = extensionManager ?? throw new ArgumentNullException(nameof(extensionManager));
            _extensions = extensions ?? throw new ArgumentNullException(nameof(extensions));
        }

        internal RimMindRuntime(
            RimMindCompositionRoot.CompositionResult composition,
            RimMindLifecycleManager lifecycleManager,
            RimMindExtensionManager extensionManager,
            ExtensionRegistryCatalog extensions)
        {
            _composition = composition ?? throw new ArgumentNullException(nameof(composition));
            _services = composition.Services;
            _lifecycleManager = lifecycleManager ?? throw new ArgumentNullException(nameof(lifecycleManager));
            _extensionManager = extensionManager ?? throw new ArgumentNullException(nameof(extensionManager));
            _extensions = extensions ?? throw new ArgumentNullException(nameof(extensions));
        }

        public void Shutdown()
        {
            _lifecycleManager.Shutdown();
            _extensionManager.ResetRuntimeLocalState();
        }

        public void Dispose() => Shutdown();

        public static void ResetInstance()
        {
            RimMindRuntimeHost.Shutdown();
        }

        public IExtensionRegistry<T> GetExtensionRegistry<T>() where T : class, IExtension
        {
            return _extensions.GetExtensionRegistry<T>();
        }

        public void AddMiddleware<TContext>(IMiddleware<TContext> middleware) where TContext : IPipelineContext
        {
            _extensionManager.AddMiddleware(
                middleware,
                BusPublishPipeline,
                UnifiedPipeline);
        }

        public void RegisterAgentIdentityProvider(Func<Pawn, AgentIdentity?> provider)
            => _extensionManager.RegisterAgentIdentityProvider(provider);

        public AgentIdentity? GetAgentIdentity(Pawn pawn)
            => _extensionManager.GetAgentIdentity(pawn);

        public void RegisterAgentActionBridge(IAgentActionBridge bridge)
            => _extensionManager.RegisterAgentActionBridge(bridge);

        public IAgentActionBridge GetAgentActionBridge() => _extensionManager.GetAgentActionBridge();

        public void RegisterParameterTuner(IParameterTuner tuner)
            => _extensionManager.RegisterParameterTuner(tuner);

        public IAIClient? GetClient() => ClientManager.GetClient();
        public void InvalidateClientCache() => ClientManager.InvalidateCache();
        public IAIClient? GetPlayer2Client() => ClientManager.GetPlayer2Client();
        public ISettingsProvider? GetSettingsProvider() => ResolveOptional<ISettingsProvider>(c => c.SettingsProvider);
    }
}
