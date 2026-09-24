using System;
using System.Threading;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Domain.Events;

namespace RimMind.Application.Common.Defaults;

public sealed class AgentBusCoreSubscriber : IDisposable
{
    private readonly IAgentBus _eventBus;
    private readonly ILogSink _logSink;
    private readonly string _perceptionKey;
    private readonly string _actionKey;
    private readonly string _modeChangedKey;
    private readonly string _lifecycleKey;
    private readonly string _decisionKey;
    private readonly string _goalKey;
    private int _disposed;

    public AgentBusCoreSubscriber(IAgentBus eventBus, ILogSink logSink)
    {
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _logSink = logSink;
        _perceptionKey = eventBus.Subscribe<PerceptionEvent>(OnPerception);
        _actionKey = eventBus.Subscribe<ActionEvent>(OnAction);
        _modeChangedKey = eventBus.Subscribe<AgentModeChangedEvent>(OnModeChanged);
        _lifecycleKey = eventBus.Subscribe<AgentLifecycleEvent>(OnLifecycle);
        _decisionKey = eventBus.Subscribe<DecisionEvent>(OnDecision);
        _goalKey = eventBus.Subscribe<GoalEvent>(OnGoal);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _eventBus.Unsubscribe<PerceptionEvent>(_perceptionKey);
        _eventBus.Unsubscribe<ActionEvent>(_actionKey);
        _eventBus.Unsubscribe<AgentModeChangedEvent>(_modeChangedKey);
        _eventBus.Unsubscribe<AgentLifecycleEvent>(_lifecycleKey);
        _eventBus.Unsubscribe<DecisionEvent>(_decisionKey);
        _eventBus.Unsubscribe<GoalEvent>(_goalKey);
    }

    private void OnPerception(PerceptionEvent e)
    {
        _logSink.Message($"[AgentBus] Perception: NpcId={e.NpcId}, PawnId={e.PawnId}, Type={e.PerceptionType}");
    }

    private void OnAction(ActionEvent e)
    {
        _logSink.Message($"[AgentBus] Action: NpcId={e.NpcId}, PawnId={e.PawnId}, Name={e.ActionName}, Success={e.Success}");
    }

    private void OnModeChanged(AgentModeChangedEvent e)
    {
        _logSink.Message($"[AgentBus] ModeChanged: NpcId={e.NpcId}, PawnId={e.PawnId}, {e.OldMode}->{e.NewMode}");
    }

    private void OnLifecycle(AgentLifecycleEvent e)
    {
        _logSink.Message($"[AgentBus] Lifecycle: NpcId={e.NpcId}, PawnId={e.PawnId}, {e.PreviousState}->{e.NewState}");
    }

    private void OnDecision(DecisionEvent e)
    {
        _logSink.Message($"[AgentBus] Decision: NpcId={e.NpcId}, PawnId={e.PawnId}, Type={e.DecisionType}");
    }

    private void OnGoal(GoalEvent e)
    {
        _logSink.Message($"[AgentBus] Goal: NpcId={e.NpcId}, PawnId={e.PawnId}, Status={e.Status}, Desc={e.GoalDescription}");
    }
}
