using System;
using System.Collections.Generic;
using System.Threading;
using RimMind.Application.Common.Behaviours;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Agent.Modes;
using RimMind.Application.Common.Interfaces.Agent.Psychology;
using RimMind.Application.Common.Interfaces.Extension;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Interfaces.Pipeline;
using RimMind.Application.Common.Models.Agent;
using RimMind.Application.Common.Defaults;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Application.Features.Agent.Modes;
using Verse;
using RimMind.Presentation.Runtime.Services;

namespace RimMind.Presentation.Runtime
{
    /// <summary>
    /// Extension Manager: responsible for extension registration, middleware, agent identity, and action bridge.
    /// Extracted from RimMindRuntime to satisfy SRP.
    /// </summary>
    internal sealed class RimMindExtensionManager
    {
        private readonly ILogSink? _logSink;
        private readonly ITickProvider? _tickProvider;
        private readonly IAgentBus _agentBus;
        private readonly AgentActionBridgeSlot _actionBridge;
        private readonly IPsychologyWatcher? _psychologyWatcher;
        private readonly object _parameterTunerSync = new object();
        private readonly Dictionary<string, IParameterTuner> _parameterTuners =
            new Dictionary<string, IParameterTuner>(StringComparer.Ordinal);

        private AgentBusCoreSubscriber? _coreSubscriber;
        private volatile Func<Pawn, AgentIdentity?>? _agentIdentityProvider;
        private IReadOnlyList<IParameterTuner> _parameterTunerSnapshot = Array.Empty<IParameterTuner>();

        public Func<Pawn, AgentIdentity?>? AgentIdentityProvider => _agentIdentityProvider;
        public IAgentActionBridge AgentActionBridge => _actionBridge.Current;
        public IReadOnlyList<IParameterTuner> ParameterTuners => Volatile.Read(ref _parameterTunerSnapshot);

        public RimMindExtensionManager(
            ILogSink? logSink,
            ITickProvider? tickProvider,
            IAgentBus agentBus,
            AgentActionBridgeSlot actionBridge,
            IPsychologyWatcher? psychologyWatcher = null)
        {
            _logSink = logSink;
            _tickProvider = tickProvider;
            _agentBus = agentBus;
            _actionBridge = actionBridge ?? throw new ArgumentNullException(nameof(actionBridge));
            _psychologyWatcher = psychologyWatcher;
        }

        public void RegisterBuiltinModes(IExtensionRegistry<IAgentMode> modeRegistry)
        {
            var tickProvider = _tickProvider
                ?? throw new InvalidOperationException("ITickProvider not registered");
            modeRegistry.Register(new ReactiveAgentMode());
            modeRegistry.Register(new ProactiveAgentMode(
                tickProvider,
                psychologyWatcher: _psychologyWatcher));
        }

        public void RegisterCoreSubscribers()
        {
            var logSink = _logSink ?? throw new InvalidOperationException("ILogSink not registered");
            _coreSubscriber = new AgentBusCoreSubscriber(_agentBus, logSink);
        }

        public void RegisterAgentIdentityProvider(Func<Pawn, AgentIdentity?> provider)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));

            var previous = Interlocked.Exchange(ref _agentIdentityProvider, provider);
            if (previous != null)
            {
                _logSink?.Warning(
                    $"[RimMindExtensionManager] event=agent_identity_provider_replaced " +
                    $"previous_method={DescribeProvider(previous)} " +
                    $"replacement_method={DescribeProvider(provider)}");
            }
        }

        public AgentIdentity? GetAgentIdentity(Pawn pawn)
            => _agentIdentityProvider?.Invoke(pawn);

        public void RegisterAgentActionBridge(IAgentActionBridge bridge)
        {
            _actionBridge.Replace(bridge, _logSink);
        }

        public IAgentActionBridge GetAgentActionBridge() => _actionBridge.Current;

        public void RegisterParameterTuner(IParameterTuner tuner)
        {
            if (tuner == null) throw new ArgumentNullException(nameof(tuner));

            IParameterTuner? previous;
            lock (_parameterTunerSync)
            {
                _parameterTuners.TryGetValue(tuner.TunerId, out previous);
                _parameterTuners[tuner.TunerId] = tuner;
                var snapshot = new IParameterTuner[_parameterTuners.Count];
                _parameterTuners.Values.CopyTo(snapshot, 0);
                Volatile.Write(ref _parameterTunerSnapshot, Array.AsReadOnly(snapshot));
            }

            if (previous != null)
            {
                _logSink?.Warning(
                    $"[RimMindExtensionManager] event=parameter_tuner_replaced " +
                    $"tuner_id={tuner.TunerId} previous_owner={previous.OwnerModId} " +
                    $"replacement_owner={tuner.OwnerModId}");
            }
        }

        public void AddMiddleware<TContext>(
            IMiddleware<TContext> middleware,
            IPipeline<BusPublishContext>? busPipeline,
            IPipeline<LlmRequestContext>? llmPipeline) where TContext : IPipelineContext
        {
            if (middleware == null) return;
            bool added = false;
            if (busPipeline is MutablePipeline<BusPublishContext> busPipe && middleware is IMiddleware<BusPublishContext> busMw)
            { busPipe.Use(busMw); added = true; }

            if (llmPipeline is MutablePipeline<LlmRequestContext> llmPipe && middleware is IMiddleware<LlmRequestContext> llmMw)
            { llmPipe.Use(llmMw); added = true; }

            if (!added)
            {
                _logSink?.Warning($"[RimMindExtensionManager] AddMiddleware: no pipeline found for TContext={typeof(TContext).Name}, middleware={middleware.Name}");
            }
        }

        public void Reset()
        {
            ResetRuntimeLocalState();
            _actionBridge.Reset();
        }

        public void ResetRuntimeLocalState()
        {
            Interlocked.Exchange(ref _agentIdentityProvider, null);
            lock (_parameterTunerSync)
            {
                _parameterTuners.Clear();
                Volatile.Write(ref _parameterTunerSnapshot, Array.Empty<IParameterTuner>());
            }
        }

        private static string DescribeProvider(Delegate provider)
        {
            var declaringType = provider.Method.DeclaringType;
            return $"{declaringType?.FullName ?? "unknown"}.{provider.Method.Name}";
        }
    }
}
