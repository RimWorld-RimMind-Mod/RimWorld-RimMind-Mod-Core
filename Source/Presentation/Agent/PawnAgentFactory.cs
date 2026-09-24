using System;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Interfaces.Agent.Perception;
using RimMind.Application.Common.Interfaces.Agent.Psychology;
using RimMind.Application.Common.Interfaces.Agent.Social;
using RimMind.Application.Common.Interfaces.Async;
using RimMind.Application.Common.Interfaces.Extension;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Features.Agent.InnerVoice;
using Verse;

namespace RimMind.Presentation.Agent
{
    public class PawnAgentFactory : IPawnAgentFactoryVerse
    {
        private readonly IAgentTickSettings? _tickSettings;
        private readonly IAgentBus _agentBus;
        private readonly IActionExecutor _actionExecutor;
        private readonly ILogSink? _log;
        private readonly IExtensionRegistry<IPerceptionSource>? _perceptionSourceRegistry;
        private readonly InnerVoiceHandler? _innerVoiceHandler;
        private readonly IPsychologyWatcher? _psychologyWatcher;
        private readonly ITickProvider _tickProvider;
        private readonly IDreamGenerator? _dreamGenerator;
        private readonly IDreamThoughtInjector? _dreamThoughtInjector;
        private readonly ITraitEvolver _traitEvolver;
        private readonly ICompletionFence _completionFence;

        internal IAgentTickSettings? TickSettings => _tickSettings;
        internal IAgentBus AgentBus => _agentBus;
        internal IActionExecutor ActionExecutor => _actionExecutor;
        internal ILogSink? LogSink => _log;
        internal InnerVoiceHandler? InnerVoiceHandler => _innerVoiceHandler;
        internal IPsychologyWatcher? PsychologyWatcher => _psychologyWatcher;
        internal ITickProvider TickProvider => _tickProvider;
        internal IDreamGenerator? DreamGenerator => _dreamGenerator;
        internal IDreamThoughtInjector? DreamThoughtInjector => _dreamThoughtInjector;
        internal ITraitEvolver TraitEvolver => _traitEvolver;
        internal ICompletionFence CompletionFence => _completionFence;

        internal PawnAgentFactory(
            IAgentTickSettings? tickSettings,
            IAgentBus agentBus,
            IActionExecutor actionExecutor,
            InnerVoiceHandler? innerVoiceHandler,
            IPsychologyWatcher? psychologyWatcher,
            ITickProvider tickProvider,
            IDreamThoughtInjector? dreamThoughtInjector,
            ITraitEvolver traitEvolver,
            ILogSink? log,
            IExtensionRegistry<IPerceptionSource>? perceptionSourceRegistry,
            ICompletionFence completionFence,
            IDreamGenerator? dreamGenerator = null)
        {
            _tickSettings = tickSettings;
            _agentBus = agentBus;
            _actionExecutor = actionExecutor;
            _innerVoiceHandler = innerVoiceHandler;
            _psychologyWatcher = psychologyWatcher;
            _tickProvider = tickProvider ?? throw new ArgumentNullException(nameof(tickProvider));
            _dreamGenerator = dreamGenerator;
            _dreamThoughtInjector = dreamThoughtInjector;
            _traitEvolver = traitEvolver ?? throw new ArgumentNullException(nameof(traitEvolver));
            _log = log;
            _perceptionSourceRegistry = perceptionSourceRegistry;
            _completionFence = completionFence ?? throw new ArgumentNullException(nameof(completionFence));
        }

        public IPawnAgent Create(Pawn pawn, IAgentBus agentBus)
        {
            var agent = new PawnAgent(pawn, _tickSettings!, agentBus, log: _log);
            agent.RebuildCollaborators(
                new PawnPerceiver(agent, agentBus, _perceptionSourceRegistry),
                new PawnThinker(agent, _tickSettings!, agentBus, _innerVoiceHandler, _psychologyWatcher, _tickProvider, _dreamGenerator, _dreamThoughtInjector, _traitEvolver, _log, _completionFence),
                new PawnActor(agent, _actionExecutor),
                new PawnRecorder(agent, agentBus));
            return agent;
        }

        public void SerializeAgent(ref IPawnAgent? agent, string label)
        {
            // Verse Scribe_Deep.Look requires concrete PawnAgent type, not interface.
            // Encapsulate the type conversion within PawnAgent.Serialize/Deserialize.
            PawnAgent.Serialize(ref agent, label, this);
        }
    }
}
