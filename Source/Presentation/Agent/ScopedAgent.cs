using System;
using System.Collections.Generic;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Interfaces.Agent.Modes;
using RimMind.Application.Common.Interfaces.Extension;
using RimMind.Application.Common.Models.Agent;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Application.Common.Models.Tools;
using RimMind.Domain.Agent.Modes;
using RimMind.Domain.Enums;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;
using RimMind.Presentation.Api;

namespace RimMind.Presentation.Agent
{
    public sealed class ScopedAgent : IScopedAgent
    {
        private const int ThinkCooldownTicks = 300;

        public AgentScope Scope { get; }
        public string ScopeId => Scope.Id;
        public string ScopeType => Scope.ScopeType;
        public int? MapId => Scope.MapId;

        private readonly IAgentBus _agentBus;
        private AgentState _state = AgentState.Dormant;
        private AgentModeId _currentModeId = AgentModeId.Reactive;
        private readonly List<BehaviorRecordDto> _behaviorHistory = new();
        private int _lastThinkTick;
        private volatile bool _thinking;
        private volatile bool _hasPendingCallback;
        private Result<AgentDecision, RimMindError> _pendingDecision;
        private int _requestSentTick;

        public ScopedAgent(AgentScope scope, IAgentBus agentBus)
        {
            Scope = scope ?? AgentScope.Custom("unknown", "unknown");
            _agentBus = agentBus;
        }

        public ScopedAgent(string scopeId, string scopeType, IAgentBus agentBus, int? mapId = null)
            : this(AgentScope.Custom(scopeType, scopeId, mapId), agentBus)
        {
        }

        public bool IsActive => _state == AgentState.Active;
        public AgentState State => _state;
        public string NpcId => ScopeId;
        public string Label => $"{ScopeType}:{ScopeId}";
        int? IAgentInfo.LastThinkTick { get => _lastThinkTick > 0 ? _lastThinkTick : null; set => _lastThinkTick = value ?? 0; }
        public int GoalCount => 0;
        public AgentModeId CurrentModeId => _currentModeId;
        public IAgentMode CurrentMode => new ScopedAgentMode(_currentModeId, ScopeType);
        public bool IsPawnValid => false;

        public bool TransitionTo(AgentState newState)
        {
            _state = newState;
            return true;
        }

        public void Tick()
        {
            if (_state != AgentState.Active) return;

            if (_hasPendingCallback)
            {
                _hasPendingCallback = false;
                ProcessPendingCallback();
            }

            if (_thinking && _requestSentTick > 0)
            {
                int elapsed = Verse.Find.TickManager.TicksGame - _requestSentTick;
                if (elapsed > 6000)
                {
                    _thinking = false;
                    _requestSentTick = 0;
                }
            }

            if (_thinking) return;

            int now = Verse.Find.TickManager.TicksGame;
            if (now - _lastThinkTick < ThinkCooldownTicks) return;

            _lastThinkTick = now;
            Think();
        }

        public void ForceThink()
        {
            _lastThinkTick = 0;
            if (_state == AgentState.Active && !_thinking)
                Think();
        }

        public void SwitchMode(AgentModeId modeId) => _currentModeId = modeId;
        public void Cleanup() => _state = AgentState.Terminated;
        public void Destroy() => _state = AgentState.Terminated;
        public void ResubscribeEvents() { }

        public bool RemoveGoal(string goalDescription) => false;

        public void RecordBehavior(BehaviorRecordDto record)
        {
            _behaviorHistory.Add(record);
            if (_behaviorHistory.Count > 100)
                _behaviorHistory.RemoveAt(0);
        }

        public IReadOnlyList<BehaviorRecordDto> GetRecentHistory(int count = 10)
        {
            int start = System.Math.Max(0, _behaviorHistory.Count - count);
            return _behaviorHistory.GetRange(start, _behaviorHistory.Count - start);
        }

        public float GetRecentSuccessRate(int count = 10)
        {
            var recent = GetRecentHistory(count);
            if (recent.Count == 0) return 0f;
            int successes = 0;
            foreach (var r in recent)
                if (r.Success) successes++;
            return (float)successes / recent.Count;
        }

        public string GetDebugInfo()
        {
            return $"[ScopedAgent] Scope={ScopeType}:{ScopeId} State={_state} Mode={_currentModeId} Thinking={_thinking}";
        }

        object? IJobProvider.ConsumePendingJob() => null;

        private void Think()
        {
            _thinking = true;
            try
            {
                var mode = CurrentMode;
                var perceptions = Array.Empty<PerceptionBufferEntry>();
                if (!mode.ShouldThink(this, perceptions))
                {
                    _thinking = false;
                    return;
                }

                var strategy = mode.GetThinkStrategy();
                var availableTools = new List<ToolDefinition>();
                var envelope = strategy.BuildEnvelope(this, perceptions, availableTools);
                _requestSentTick = Verse.Find.TickManager.TicksGame;

                RimMindAPI.Request.Send(envelope, result =>
                {
                    var decisionResult = strategy.ParseDecision(this, result.IsOk ? result.Value : new LlmResponse { Content = "" });
                    _pendingDecision = decisionResult;
                    _hasPendingCallback = true;
                });
            }
            catch (Exception)
            {
                _thinking = false;
            }
        }

        private void ProcessPendingCallback()
        {
            _thinking = false;
            _requestSentTick = 0;

            if (_pendingDecision.IsOk)
            {
                var decision = _pendingDecision.Value;
                RecordBehavior(new BehaviorRecordDto
                {
                    Action = decision.ActionIntent ?? "think",
                    Success = true,
                    Timestamp = Verse.Find.TickManager.TicksGame,
                });
            }
            else
            {
                RecordBehavior(new BehaviorRecordDto
                {
                    Action = "think_failed",
                    Success = false,
                    Timestamp = Verse.Find.TickManager.TicksGame,
                });
            }
        }

        private sealed class ScopedAgentMode : IAgentMode
        {
            private readonly string _scopeType;
            public AgentModeId ModeId { get; }
            public string DisplayName => ModeId.Value;
            public string Description => $"Scoped agent mode: {ModeId.Value}";
            string IExtension.Id => ModeId.Value;
            string IExtension.OwnerModId => "RimMind.Core";

            public ScopedAgentMode(AgentModeId modeId, string scopeType)
            {
                ModeId = modeId;
                _scopeType = scopeType;
            }

            public bool IsApplicable(IAgentInfo agent) => true;
            public bool ShouldThink(IAgentInfo agent, IReadOnlyList<PerceptionBufferEntry> perceptions) => perceptions.Count > 0 || agent.State == AgentState.Active;
            public IThinkStrategy GetThinkStrategy() => new ScopedThinkStrategy(_scopeType);
            public IReadOnlyList<string> AllowedToolIds(Application.Common.Interfaces.Tools.IToolRegistry registry) => new List<string>();
        }
    }
}
