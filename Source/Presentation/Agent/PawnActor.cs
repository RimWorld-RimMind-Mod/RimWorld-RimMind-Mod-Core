using System;
using System.Collections.Generic;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Interfaces.Extension;
using RimMind.Application.Common.Models;
using RimMind.Application.Common.Models.Client;
using RimMind.Application.Common.Models.Npc;
using RimMind.Domain.Agent.Modes;
using RimMind.Domain.Common;
using RimMind.Domain.Enums;
using RimMind.Domain.ValueObjects;
using RimMind.Presentation.Runtime;
using Verse;
using Verse.AI;

namespace RimMind.Presentation.Agent
{
    public class PawnActor : IPawnActorVerse
    {
        private readonly IPawnAgentVerse _agent;
        private readonly IActionExecutor? _actionExecutor;
        private Verse.AI.Job? _pendingJob;
        private int _lastActionTick;
        private int _actionCooldown = RimMindDefaults.DefaultActionCooldown;

        public PawnActor(IPawnAgentVerse agent, IActionExecutor? actionExecutor = null)
        {
            _agent = agent ?? throw new ArgumentNullException(nameof(agent));
            _actionExecutor = actionExecutor;
        }

        public bool HasPendingJob => _pendingJob != null;

        public void SetPendingJob(Verse.AI.Job job)
        {
            _pendingJob = job;
        }

        public Verse.AI.Job? ConsumePendingJob()
        {
            var job = _pendingJob;
            _pendingJob = null;
            return job;
        }

        public void Tick()
        {
            if (_agent.State != AgentState.Active) return;
            if (_pendingJob != null) return;
            if (Find.TickManager.TicksGame - _lastActionTick < _actionCooldown) return;
        }

        public bool TryExecuteAction(string actionId, string? target = null)
        {
            if (string.IsNullOrEmpty(actionId)) return false;

            if (_actionExecutor == null || !_actionExecutor.CanExecute(actionId))
            {
                _lastActionTick = Find.TickManager.TicksGame;
                return false;
            }

            var decision = new AgentDecision(
                ActionIntent: actionId,
                Reason: "",
                TargetPawnId: target,
                Param: null);

            var result = ExecuteDecision(decision);
            _lastActionTick = Find.TickManager.TicksGame;
            return result.IsOk;
        }

        public Result<Unit, RimMindError> ExecuteDecision(AgentDecision decision)
        {
            if (decision == null)
                return Result<Unit, RimMindError>.Err(RimMindErrors.Internal("AgentDecision is null"));

            if (_actionExecutor == null)
                return Result<Unit, RimMindError>.Err(RimMindErrors.Internal("IActionExecutor not available"));

            return _actionExecutor.ExecuteDecision(decision, _agent.Pawn.thingIDNumber);
        }
    }
}
