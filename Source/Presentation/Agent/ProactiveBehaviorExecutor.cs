using System;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Interfaces.Agent.Modes;
using RimMind.Application.Common.Interfaces.Agent.Social;
using RimMind.Application.Common.Interfaces.Async;
using RimMind.Application.Common.Interfaces.Extension;
using RimMind.Application.Features.Agent;
using RimMind.Presentation.Api;
using Verse;

namespace RimMind.Presentation.Agent
{
    internal sealed class ProactiveBehaviorExecutor
    {
        private readonly IAgentBus _agentBus;
        private readonly IDreamGenerator? _dreamGenerator;
        private readonly IDreamThoughtInjector? _dreamThoughtInjector;
        private readonly ITraitEvolver _traitEvolver;
        private readonly ILogSink? _log;
        private readonly ICompletionFence _completionFence;
        private readonly IExtensionRegistry<IProactiveBehavior>? _behaviorRegistry;
        private ProactiveBehaviorOrchestrator? _cachedOrchestrator;
        private IProactiveExtensions? _cachedMode;
        private int _cachedPawnId = int.MinValue;

        public ProactiveBehaviorExecutor(
            IAgentBus agentBus,
            IDreamGenerator? dreamGenerator,
            IDreamThoughtInjector? dreamThoughtInjector,
            ITraitEvolver traitEvolver,
            ILogSink? log,
            ICompletionFence completionFence,
            IExtensionRegistry<IProactiveBehavior>? behaviorRegistry = null)
        {
            _agentBus = agentBus ?? throw new ArgumentNullException(nameof(agentBus));
            _dreamGenerator = dreamGenerator;
            _dreamThoughtInjector = dreamThoughtInjector;
            _traitEvolver = traitEvolver ?? throw new ArgumentNullException(nameof(traitEvolver));
            _log = log;
            _completionFence = completionFence ?? throw new ArgumentNullException(nameof(completionFence));
            _behaviorRegistry = behaviorRegistry;
        }

        public void ExecuteProactiveExtensions(IPawnAgent agent, IAgentMode mode, int pawnId)
        {
            if (mode is not IProactiveExtensions proactive) return;
            var behaviorRegistry = _behaviorRegistry ?? TryGetBehaviorRegistry();
            if (_cachedOrchestrator == null || !ReferenceEquals(_cachedMode, proactive) || _cachedPawnId != pawnId)
            {
                _cachedMode = proactive;
                _cachedPawnId = pawnId;
                _cachedOrchestrator = new ProactiveBehaviorOrchestrator(
                    proactive.ReflectionStrategy,
                    proactive.DailyPlanner,
                    _dreamGenerator,
                    _dreamThoughtInjector,
                    proactive.TraitEvolutionEngine,
                    _traitEvolver,
                    _agentBus,
                    pawnId,
                    _log,
                    behaviorRegistry: behaviorRegistry,
                    completionFence: _completionFence);
            }
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            _cachedOrchestrator.ExecuteAll(agent, currentTick);
        }

        private static IExtensionRegistry<IProactiveBehavior>? TryGetBehaviorRegistry()
        {
            try
            {
                return RimMindAPI.Ext.Get<IProactiveBehavior>();
            }
            catch
            {
                return null;
            }
        }
    }
}
