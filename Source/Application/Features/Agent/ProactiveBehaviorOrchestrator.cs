using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces.Async;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Interfaces.Agent.Planning;
using RimMind.Application.Common.Interfaces.Agent.Reflection;
using RimMind.Application.Common.Interfaces.Agent.Social;
using RimMind.Domain.Agent.Social;
using RimMind.Domain.Events;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Features.Agent
{
    public class ProactiveBehaviorOrchestrator : IProactiveBehaviorOrchestrator
    {
        private const float TraitConfidenceThreshold = 0.7f;

        private readonly IReflectionStrategy? _reflectionStrategy;
        private readonly IDailyPlanner? _dailyPlanner;
        private readonly IDreamGenerator? _dreamGenerator;
        private readonly IDreamThoughtInjector? _dreamThoughtInjector;
        private readonly ITraitEvolutionEngine? _traitEvolutionEngine;
        private readonly ITraitEvolver? _traitEvolver;
        private readonly IAgentBus _agentBus;
        private readonly int _pawnId;
        private readonly ILogSink? _log;
        private readonly ICompletionFence _completionFence;

        public ProactiveBehaviorOrchestrator(
            IReflectionStrategy? reflectionStrategy,
            IDailyPlanner? dailyPlanner,
            IDreamGenerator? dreamGenerator,
            IDreamThoughtInjector? dreamThoughtInjector,
            ITraitEvolutionEngine? traitEvolutionEngine,
            ITraitEvolver? traitEvolver,
            IAgentBus agentBus,
            int pawnId,
            ILogSink? log = null,
            ICompletionFence? completionFence = null)
        {
            _reflectionStrategy = reflectionStrategy;
            _dailyPlanner = dailyPlanner;
            _dreamGenerator = dreamGenerator;
            _dreamThoughtInjector = dreamThoughtInjector;
            _traitEvolutionEngine = traitEvolutionEngine;
            _traitEvolver = traitEvolver;
            _agentBus = agentBus ?? throw new ArgumentNullException(nameof(agentBus));
            _pawnId = pawnId;
            _log = log;
            _completionFence = completionFence ?? UnboundedCompletionFence.Instance;
        }

        public void ExecuteReflection(IAgentInfo agent)
        {
            if (_reflectionStrategy?.ShouldReflect(agent) != true) return;
            if (_completionFence.CancellationToken.IsCancellationRequested) return;
            _reflectionStrategy.ReflectAsync(agent, _completionFence.CancellationToken).ContinueWith(t =>
            {
                if (!_completionFence.TryAcceptCompletion()) return;
                if (t.IsFaulted)
                {
                    _log?.Warning($"[RimMind.Orchestrator] action=ReflectionFailed npcId={agent.NpcId} error={t.Exception?.InnerException?.Message}");
                    return;
                }
                if (t.Status == TaskStatus.RanToCompletion && t.Result.IsOk && t.Result.Value.Count > 0)
                    _log?.Message($"[RimMind.Orchestrator] action=ReflectionCompleted npcId={agent.NpcId} insightCount={t.Result.Value.Count}");
            }, TaskScheduler.Current);
        }

        public void ExecutePlanning(IAgentInfo agent)
        {
            if (_dailyPlanner?.ShouldPlan(agent) != true) return;
            if (_completionFence.CancellationToken.IsCancellationRequested) return;
            _dailyPlanner.PlanAsync(agent, _completionFence.CancellationToken).ContinueWith(t =>
            {
                if (!_completionFence.TryAcceptCompletion()) return;
                if (t.IsFaulted)
                {
                    _log?.Warning($"[RimMind.Orchestrator] action=PlanningFailed npcId={agent.NpcId} error={t.Exception?.InnerException?.Message}");
                    return;
                }
                if (t.Status == TaskStatus.RanToCompletion && t.Result.IsOk && t.Result.Value.Count > 0)
                    _log?.Message($"[RimMind.Orchestrator] action=PlanningCompleted npcId={agent.NpcId} blockCount={t.Result.Value.Count}");
            }, TaskScheduler.Current);
        }

        public void ExecuteDream(IAgentInfo agent)
        {
            if (_dreamGenerator?.ShouldDream(agent) != true) return;
            if (_completionFence.CancellationToken.IsCancellationRequested) return;
            _dreamGenerator.GenerateDreamAsync(agent, _completionFence.CancellationToken).ContinueWith(t =>
            {
                if (!_completionFence.TryAcceptCompletion()) return;
                if (t.IsFaulted)
                {
                    _log?.Warning($"[RimMind.Orchestrator] action=DreamFailed npcId={agent.NpcId} error={t.Exception?.InnerException?.Message}");
                    return;
                }
                if (t.Status == TaskStatus.RanToCompletion && t.Result.IsOk)
                {
                    if (!_completionFence.TryAcceptCompletion()) return;
                    _dreamThoughtInjector?.InjectDreamThought(_pawnId, t.Result.Value);
                    if (!_completionFence.TryAcceptCompletion()) return;
                    _agentBus.Publish(new DreamEvent(
                        agent.NpcId, _pawnId, t.Result.Value.DreamContent,
                        t.Result.Value.DreamType, t.Result.Value.MoodImpact));
                }
            }, TaskScheduler.Current);
        }

        public void ExecuteTraitEvolution(IAgentInfo agent)
        {
            if (_traitEvolutionEngine?.ShouldEvolve(agent) != true) return;
            if (_completionFence.CancellationToken.IsCancellationRequested) return;
            var task = _traitEvolutionEngine.EvaluateEvolutionAsync(agent, _completionFence.CancellationToken);
            if (task.IsCompleted)
            {
                ProcessTraitEvolutionResult(task, agent);
                return;
            }
            task.ContinueWith(t => ProcessTraitEvolutionResult(t, agent), TaskScheduler.Current);
        }

        private void ProcessTraitEvolutionResult(Task<Result<IReadOnlyList<TraitEvolutionRecord>, RimMindError>> t, IAgentInfo agent)
        {
            if (!_completionFence.TryAcceptCompletion()) return;
            if (t.IsFaulted)
            {
                _log?.Warning($"[RimMind.Orchestrator] action=TraitEvolutionFailed npcId={agent.NpcId} error={t.Exception?.InnerException?.Message}");
                return;
            }
            if (t.Status == TaskStatus.RanToCompletion && t.Result.IsOk)
            {
                foreach (var record in t.Result.Value.Where(r => r.Confidence >= TraitConfidenceThreshold))
                {
                    if (!_completionFence.TryAcceptCompletion()) return;
                    _traitEvolver?.ApplyTraitEvolution(_pawnId, record);
                    if (!_completionFence.TryAcceptCompletion()) return;
                    _agentBus.Publish(new TraitEvolutionEvent(
                        agent.NpcId, _pawnId, record.TraitDefName,
                        record.Kind, record.Reason, record.Confidence));
                }
            }
        }

        private sealed class UnboundedCompletionFence : ICompletionFence
        {
            public static readonly UnboundedCompletionFence Instance = new UnboundedCompletionFence();

            public CancellationToken CancellationToken => CancellationToken.None;

            public bool TryAcceptCompletion() => true;
        }
    }
}
