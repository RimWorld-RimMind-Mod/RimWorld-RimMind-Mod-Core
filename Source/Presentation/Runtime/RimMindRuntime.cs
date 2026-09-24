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
        private readonly RimMindCompositionRoot.CompositionResult _composition;
        private readonly RimMindLifecycleManager _lifecycleManager;
        private readonly RimMindExtensionManager _extensionManager;
        private readonly ExtensionRegistryCatalog _extensions;

        // Public properties — delegate to CompositionResult
        public IAgentBus AgentBus => _composition.AgentBus;
        public IContextEngine ContextEngine => _composition.ContextEngine;
        public IHistoryManager HistoryManager => _composition.HistoryManager;
        public IClientManager ClientManager => _composition.ClientManager;
        public IAudioPlayer AudioPlayer => _composition.AudioPlayer;
        public IProviderRegistry ProviderRegistry => _composition.ProviderRegistry;
        public IOverlayService OverlayService => _composition.OverlayService;
        public IRequestQueue Queue => _composition.Queue;
        public ITelemetryCollector Telemetry => _composition.Telemetry;
        public IToolRegistry ToolRegistry => _composition.ToolRegistry;
        public IGameMechanismRegistry MechanismRegistry => _composition.MechanismRegistry;
        public IWindowService? WindowService => _composition.WindowService;
        public IContextKeyRegistry ContextKeys => _composition.ContextKeyRegistry;
        public IRelevanceTable RelevanceTable => _composition.RelevanceTable;
        public IRelevanceLearner ContextLearner => _composition.RelevanceLearner;
        public IGameContextBuilder GameContextBuilder => _composition.GameContextBuilder;

        public IPipeline<BusPublishContext> BusPublishPipeline => _composition.BusPublishPipeline;
        public IPipeline<LlmRequestContext> UnifiedPipeline => _composition.UnifiedPipeline;

        public IReadOnlyList<IParameterTuner> ParameterTunersList => _extensionManager.ParameterTuners;
        public Func<Pawn, AgentIdentity?>? AgentIdentityProvider => _extensionManager.AgentIdentityProvider;
        public IAgentActionBridge AgentActionBridge => _extensionManager.AgentActionBridge;
        public bool IsShutdown => _lifecycleManager.IsShutdown;

        internal RimMindRuntime(
            RimMindCompositionRoot.CompositionResult composition,
            RimMindLifecycleManager lifecycleManager,
            RimMindExtensionManager extensionManager,
            ExtensionRegistryCatalog extensions)
        {
            _composition = composition ?? throw new ArgumentNullException(nameof(composition));
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
                _composition.BusPublishPipeline,
                _composition.UnifiedPipeline);
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
        public ISettingsProvider? GetSettingsProvider() => _composition.SettingsProvider;
    }
}
