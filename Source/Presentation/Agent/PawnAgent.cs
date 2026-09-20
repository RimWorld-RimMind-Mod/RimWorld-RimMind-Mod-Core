using System;
using System.Collections.Generic;
using System.Linq;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Interfaces.Agent.Modes;
using RimMind.Application.Common.Models.Agent;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Domain.Agent.Modes;
using RimMind.Domain.Common;
using RimMind.Domain.Enums;
using RimMind.Domain.Events;
using RimMind.Domain.ValueObjects;
using RimMind.Presentation.Api;
using RimMind.Presentation.Runtime;
using RimMind.Application.Common.Interfaces.Internal;
using RimWorld;
using Verse;
using Verse.AI;
using RimMind.Application.Common.Models.UI;

namespace RimMind.Presentation.Agent
{
    public class PawnAgent : IPawnAgentVerse
    {
        public Pawn Pawn { get; }
        private AgentState _state = AgentState.Dormant;
        public AgentState State { get => _state; private set => _state = value; }
        private AgentWorkflowPhase _workflowPhase = AgentWorkflowPhase.Idle;
        public AgentWorkflowPhase WorkflowPhase { get => _workflowPhase; private set => _workflowPhase = value; }
        private AgentIdentity _identity = null!;
        public AgentIdentity Identity { get => _identity; private set => _identity = value; }
        private SerializableAgentGoalStack _goalStack = new SerializableAgentGoalStack();
        public AgentGoalStack GoalStack => _goalStack;
        private StrategyOptimizer _strategyOptimizer = new StrategyOptimizer();
        public IStrategyOptimizer StrategyOptimizer => _strategyOptimizer;
        private readonly PerceptionBuffer _perceptionBuffer = new PerceptionBuffer();
        public IPerceptionBuffer PerceptionBuffer => _perceptionBuffer;
        public bool IsActive => State == AgentState.Active;
        public bool IsPawnValid => Pawn != null && !Pawn.Dead;

        private AgentModeId _currentModeId = AgentModeId.Reactive;
        public AgentModeId CurrentModeId => _currentModeId;
        public IAgentMode CurrentMode
            => RimMindAPI.Modes.FindById(_currentModeId.Value)
               ?? throw new InvalidOperationException($"AgentMode '{_currentModeId}' not registered");
        public int? LastThinkTick { get; set; }
        string IAgentInfo.NpcId => Identity.NpcId;
        string IAgentInfo.Label => Pawn?.Label ?? Identity.DisplayName;
        int IAgentInfo.GoalCount => GoalStack.TotalCount;

        private IPawnPerceiver _perceiver;
        private IPawnThinker _thinker;
        private IPawnActorVerse _actor;
        private IPawnRecorder _recorder;
        private readonly IAgentTickSettings? _tickSettings;
        private readonly IAgentBus _agentBus;
        private readonly ILogSink? _log;
        private int _lastTick;
        private int _lastThinkTick;
        private int TickInterval => _tickSettings?.AgentTickInterval ?? 150;
        private AgentAutonomyLevel _autonomyLevel = AgentAutonomyLevel.Autonomous;
        public AgentAutonomyLevel AutonomyLevel
        {
            get => _tickSettings?.AutonomyLevel ?? _autonomyLevel;
            set
            {
                _autonomyLevel = value;
                if (_tickSettings != null) _tickSettings.AutonomyLevel = value;
            }
        }

        IReadOnlyList<BehaviorRecord> IPawnAgent.BehaviorHistory => _recorder.History;

        public IReadOnlyList<BehaviorRecord> GetRecentHistory(int count = 10) => _recorder.GetRecentHistory(count);
        public float GetRecentSuccessRate(int count = 10) => _recorder.GetRecentSuccessRate(count);

        IReadOnlyList<BehaviorRecordDto> IAgentInfo.GetRecentHistory(int count)
        {
            return _recorder.GetRecentHistory(count)
                .Select(r => new BehaviorRecordDto
                {
                    Action = r.Action,
                    Reason = r.Reason,
                    Success = r.Success,
                    ResultReason = r.ResultReason,
                    GoalProgressDelta = r.GoalProgressDelta,
                    Timestamp = r.Timestamp,
                    ActionEventId = r.ActionEventId,
                    DurationMs = r.DurationMs
                })
                .ToList();
        }

        public PawnAgent(Pawn pawn, IAgentBus agentBus)
            : this(pawn, null!, agentBus) { }

        public PawnAgent(Pawn pawn, IAgentTickSettings tickSettings, IAgentBus agentBus,
            IPawnPerceiver? perceiver = null, IPawnThinker? thinker = null,
            IPawnActorVerse? actor = null, IPawnRecorder? recorder = null,
            ILogSink? log = null)
        {
            Pawn = pawn ?? throw new ArgumentNullException(nameof(pawn));
            _tickSettings = tickSettings;
            _agentBus = agentBus ?? throw new ArgumentNullException(nameof(agentBus));
            _log = log;
            _goalStack.SetAgentBus(_agentBus);
            _perceiver = perceiver!;
            _thinker = thinker!;
            _actor = actor!;
            _recorder = recorder!;
            Identity = new SerializableAgentIdentity($"NPC-{pawn.thingIDNumber}", pawn.thingIDNumber, pawn.Name?.ToStringFull ?? pawn.Label ?? "Unknown");
        }

        internal void RebuildCollaborators(IPawnPerceiver perceiver, IPawnThinker thinker, IPawnActorVerse actor, IPawnRecorder recorder)
        {
            _perceiver = perceiver ?? throw new ArgumentNullException(nameof(perceiver));
            _thinker = thinker ?? throw new ArgumentNullException(nameof(thinker));
            _actor = actor ?? throw new ArgumentNullException(nameof(actor));
            _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));

            // Restore last think tick from serialized state
            if (_lastThinkTick > 0 && _thinker is PawnThinker concreteThinker)
                concreteThinker.RestoreLastThinkTick(_lastThinkTick);
        }

        public void Tick()
        {
            if (State != AgentState.Active) return;
            if (Pawn == null || Pawn.Dead)
            {
                TransitionTo(AgentState.Terminated);
                return;
            }

            int now = Find.TickManager.TicksGame;
            if (now - _lastTick < TickInterval) return;
            _lastTick = now;

            GoalStack.CheckExpired(Pawn.thingIDNumber, now);

            // Phase-driven workflow: only one phase active at a time
            switch (WorkflowPhase)
            {
                case AgentWorkflowPhase.Idle:
                    _perceiver.Tick();
                    if (_thinker.ShouldThink())
                        TransitionWorkflow(AgentWorkflowPhase.Thinking);
                    break;

                case AgentWorkflowPhase.Thinking:
                    _thinker.Tick();
                    // Transition to Acting happens in ProcessPendingCallback when decision is ready
                    break;

                case AgentWorkflowPhase.Acting:
                    _actor.Tick();
                    // Transition to Recording after action execution
                    TransitionWorkflow(AgentWorkflowPhase.Recording);
                    break;

                case AgentWorkflowPhase.Recording:
                    // Recording is handled by RecordBehavior calls from PawnThinker
                    TransitionWorkflow(AgentWorkflowPhase.Idle);
                    break;

                case AgentWorkflowPhase.Perceiving:
                    _perceiver.Tick();
                    TransitionWorkflow(AgentWorkflowPhase.Idle);
                    break;
            }

            StrategyOptimizer.DecayAll();
        }

        /// <summary>
        /// Transition the workflow phase with guard checks.
        /// </summary>
        public void TransitionWorkflow(AgentWorkflowPhase target)
        {
            var previous = _workflowPhase;
            _workflowPhase = target;
            _agentBus?.Publish(new AgentBusEvent(
                Identity.NpcId,
                Pawn?.thingIDNumber ?? -1,
                AgentBusEventType.WorkflowPhaseChange));
        }

        public bool TransitionTo(AgentState newState)
        {
            if (!AgentStateTransition.CanTransition(State, newState)) return false;
            var previousState = State;
            State = newState;

            // On Pause: reset workflow phase and thinker state
            if (newState == AgentState.Paused)
            {
                _workflowPhase = AgentWorkflowPhase.Idle;
                _thinker.ResetThinking();
            }

            _agentBus?.Publish(new AgentLifecycleEvent(
                Identity.NpcId,
                Pawn?.thingIDNumber ?? -1,
                previousState.ToString(),
                newState.ToString()));

            return true;
        }

        public void AddGoal(AgentGoal goal)
        {
            if (goal != null && goal is not SerializableAgentGoal)
                _log?.Warning($"[RimMind.Agent] action=NonSerializableGoal npcId={Identity.NpcId} goal={goal.Description}");
            GoalStack.TryAdd(goal, Pawn.thingIDNumber);
        }

        public void ForceThink()
        {
            _thinker.ForceThink();
        }

        public Verse.AI.Job? ConsumePendingJob()
        {
            return _actor.ConsumePendingJob();
        }

        object? IJobProvider.ConsumePendingJob() => ConsumePendingJob();

        public void SetPendingJob(Verse.AI.Job job)
        {
            _actor.SetPendingJob(job);
        }

        public Result<Unit, RimMindError> ExecuteDecision(AgentDecision decision)
        {
            // Autonomy check: should this action be auto-approved?
            var riskLevel = AssessRiskLevel(decision);
            if (_tickSettings != null && !_tickSettings.ShouldApproveAction(riskLevel))
            {
                _log?.Message($"[RimMind.Agent] action=ActionPendingApproval npcId={Identity.NpcId} risk={riskLevel} intent={decision.ActionIntent}");
                SubmitDecisionForApproval(decision, riskLevel);
                return Result<Unit, RimMindError>.Ok(Unit.Value);
            }

            return _actor.ExecuteDecision(decision);
        }

        private void SubmitDecisionForApproval(AgentDecision decision, RiskLevel riskLevel)
        {
            string approveLabel = "RimMind.Agent.Request.Approve".Translate();
            string rejectLabel = "RimMind.Agent.Request.Reject".Translate();
            string pawnLabel = Pawn?.LabelShort ?? Identity?.DisplayName ?? "Pawn";
            string intent = decision.ActionIntent ?? "action";
            string reason = decision.Reason ?? intent;

            var entry = new RequestEntry
            {
                source = "agent",
                pawn = Pawn,
                title = "RimMind.Agent.Request.RiskAction".Translate(riskLevel.ToString(), pawnLabel, intent),
                description = reason,
                systemBlocked = true,
                expireTicks = 30000,
                options = new[] { approveLabel, rejectLabel },
                callback = choice =>
                {
                    if (choice == approveLabel)
                    {
                        _log?.Message($"[RimMind.Agent] action=ApprovedActionExecuting npcId={Identity.NpcId} intent={intent}");
                        _actor.ExecuteDecision(decision);
                    }
                    else
                    {
                        _log?.Message($"[RimMind.Agent] action=RejectedAction npcId={Identity.NpcId} intent={intent}");
                    }
                },
                completionCallback = completionReason =>
                {
                    if (completionReason == RequestCompletionReason.Selected)
                        return;

                    _log?.Message($"[RimMind.Agent] action=ApprovalDismissedOrExpired npcId={Identity.NpcId} intent={intent} reason={completionReason}");
                }
            };

            try
            {
                RimMindAPI.RegisterPendingRequest(entry);
            }
            catch (Exception ex)
            {
                _log?.Warning($"[RimMind.Agent] Failed to register pending request for {Identity.NpcId}: {ex.Message}");
            }
        }

        /// <summary>
        /// Assesses the risk level of a decision based on its action intent.
        /// Conservative default — sub-mods can override via IModeTransitionPolicy
        /// or custom IActionExecutor implementations.
        /// </summary>
        private RiskLevel AssessRiskLevel(AgentDecision decision)
        {
            if (decision == null) return RiskLevel.Low;

            var intent = decision.ActionIntent?.ToLowerInvariant() ?? "";

            // Critical: actions that can cause permanent harm or game state changes
            if (intent.Contains("attack") || intent.Contains("kill") || intent.Contains("arrest"))
                return RiskLevel.Critical;

            // High: actions that significantly alter pawn state
            if (intent.Contains("surgery") || intent.Contains("banish") || intent.Contains("execute"))
                return RiskLevel.High;

            // Medium: actions that change pawn assignments or roles
            if (intent.Contains("assign") || intent.Contains("draft") || intent.Contains("trade"))
                return RiskLevel.Medium;

            return RiskLevel.Low;
        }

        public bool RemoveGoal(string goalDescription)
        {
            return GoalStack.Remove(goalDescription, Pawn.thingIDNumber);
        }

        public void RecordBehavior(BehaviorRecordDto dto)
        {
            if (dto == null) return;
            var record = new BehaviorRecord
            {
                Action = dto.Action,
                Reason = dto.Reason,
                Success = dto.Success,
                ResultReason = dto.ResultReason,
                GoalProgressDelta = dto.GoalProgressDelta,
                Timestamp = dto.Timestamp,
                ActionEventId = dto.ActionEventId,
                DurationMs = dto.DurationMs,
            };
            _recorder.Record(record);
        }

        public void SwitchMode(AgentModeId modeId)
        {
            var newMode = RimMindAPI.Modes.FindById(modeId.Value);
            if (newMode == null)
                throw new InvalidOperationException($"Mode '{modeId}' not registered");
            if (!newMode.IsApplicable(this)) return;
            if (_currentModeId == modeId) return;

            // Check transition policies
            var policies = RimMindAPI.ModePolicies?.All;
            if (policies != null)
            {
                foreach (var policy in policies)
                {
                    if (!policy.CanTransition(this, _currentModeId, modeId))
                    {
                        _log?.Warning($"[RimMind.Agent] action=ModeTransitionDenied npcId={Identity.NpcId} from={_currentModeId.Value} to={modeId.Value} reason={policy.DenyReason ?? "Policy denied"}");
                        return;
                    }
                }
            }

            var oldModeId = _currentModeId;
            _currentModeId = modeId;
            LastThinkTick = null;

            int timestamp = Find.TickManager?.TicksGame ?? 0;

            _log?.Message($"[RimMind.Agent] action=ModeChanged npcId={Identity.NpcId} oldMode={oldModeId.Value} newMode={modeId.Value}");

            var bus = _agentBus;
            if (bus != null)
            {
                bus.Publish(new AgentModeChangedEvent(
                    Identity.NpcId,
                    Pawn?.thingIDNumber ?? -1,
                    oldModeId.Value,
                    modeId.Value,
                    timestamp));
            }

            if (Current.Game != null)
            {
                var pawnLabel = Pawn?.Label ?? Identity.DisplayName;
                var newLabel = newMode.DisplayName;
                Messages.Message(
                    "RimMind.Agent.ModeChanged".Translate(pawnLabel, newLabel),
                    Pawn,
                    MessageTypeDefOf.SilentInput,
                    historical: false);
            }
        }

        public void ResubscribeEvents()
        {
            // After save/load, collaborators are rebuilt by PawnAgentFactory.SerializeAgent,
            // but event subscriptions held by external subscribers may be stale.
            // Re-publish a lifecycle event so subscribers can re-associate this agent.
            _agentBus?.Publish(new AgentLifecycleEvent(
                Identity.NpcId,
                Pawn?.thingIDNumber ?? -1,
                AgentState.Dormant.ToString(),
                State.ToString()));
        }

        public void Cleanup()
        {
            PerceptionBuffer.Clear();
        }

        public void Destroy()
        {
            // Publish final lifecycle event before cleanup
            if (State != AgentState.Terminated)
                TransitionTo(AgentState.Terminated);

            // Clean up resources
            Cleanup();

            // Note: AgentBus uses key-based subscriptions, but PawnAgent doesn't track its subscription keys.
            // The AgentBus.ClearAllSubscribers() in AgentBusGameComponent handles full cleanup on game load.
            // Individual agent unsubscription requires tracking subscription keys (future enhancement).
        }

        public string GetDebugInfo()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"State: {State}");
            sb.AppendLine($"WorkflowPhase: {WorkflowPhase}");
            sb.AppendLine($"CurrentModeId: {_currentModeId.Value}");
            sb.AppendLine($"PerceptionBuffer: {PerceptionBuffer.Count} entries");
            sb.AppendLine($"LastThinkTick: {LastThinkTick?.ToString() ?? "null"}");
            sb.AppendLine($"Goals: {GoalStack.TotalCount}");
            foreach (var g in GoalStack.Goals)
                sb.AppendLine($"  - [{g.Status}] {g.Description} (P:{g.Priority:F1})");
            sb.AppendLine($"Behavior History: {((IPawnAgent)this).BehaviorHistory.Count}");
            var topW = StrategyOptimizer.GetTopN(5);
            if (topW.Count > 0)
            {
                sb.AppendLine("Strategy Weights (Top 5):");
                foreach (var kv in topW)
                    sb.AppendLine($"  {kv.Key}: {kv.Value:F2}");
            }
            return sb.ToString();
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref _state, "agentState", AgentState.Dormant);
            Scribe_Values.Look(ref _workflowPhase, "workflowPhase", AgentWorkflowPhase.Idle);
            Scribe_Deep.Look(ref _identity, "identity");
            Scribe_Deep.Look(ref _goalStack, "goalStack");
            Scribe_Deep.Look(ref _strategyOptimizer, "strategyOptimizer");

            string _currentModeIdStr = _currentModeId.Value;
            Scribe_Values.Look(ref _currentModeIdStr, "currentModeId", AgentModeId.Reactive.Value);
            _currentModeId = AgentModeId.Normalize(_currentModeIdStr);

            Scribe_Values.Look(ref _lastThinkTick, "lastThinkTick", 0);
            Scribe_Values.Look(ref _autonomyLevel, "autonomyLevel", AgentAutonomyLevel.Autonomous);
        }

        /// <summary>
        /// Encapsulates Verse serialization type conversion (Scribe_Deep.Look requires concrete type).
        /// Called by PawnAgentFactory.SerializeAgent to keep the cast internal to PawnAgent.
        /// </summary>
        internal static void Serialize(ref IPawnAgent? agent, string label, PawnAgentFactory factory)
        {
            PawnAgent? concrete = agent as PawnAgent;
            Scribe_Deep.Look(ref concrete, label);
            agent = concrete;

            // After deserialization, collaborators are null — rebuild them
            if (Scribe.mode == LoadSaveMode.LoadingVars && concrete != null)
            {
                concrete.RebuildCollaborators(
                    new PawnPerceiver(concrete, factory.AgentBus),
                    new PawnThinker(concrete, factory.TickSettings!, factory.AgentBus, factory.InnerVoiceHandler, factory.PsychologyWatcher, factory.TickProvider, factory.DreamGenerator, factory.DreamThoughtInjector, factory.TraitEvolver, factory.LogSink, factory.CompletionFence),
                    new PawnActor(concrete, factory.ActionExecutor),
                    new PawnRecorder(concrete, factory.AgentBus));

                // Re-subscribe event bus so subscribers can re-associate this agent after save/load
                concrete.ResubscribeEvents();
            }
        }
    }
}
