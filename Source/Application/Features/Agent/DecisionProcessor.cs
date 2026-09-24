using System;
using System.Collections.Generic;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Interfaces.Agent.Modes;
using RimMind.Application.Common.Models;
using RimMind.Application.Common.Models.Agent;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Application.Common.Models.Tools;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Domain.Agent.Modes;
using RimMind.Domain.Common;
using RimMind.Domain.ValueObjects;
using RimMind.Domain.Enums;
using RimMind.Domain.Events;
using RimMind.Domain.Llm;

namespace RimMind.Application.Features.Agent
{
    /// <summary>
    /// Processes AI callback results: validates, handles agentic loop continuation,
    /// executes decisions, records behavior, and publishes events.
    /// Extracted from PawnThinker.ProcessPendingCallback for single-responsibility.
    /// </summary>
    public class DecisionProcessor : IDecisionProcessor
    {
        private readonly IAgentControl _agent;
        private readonly IAgentBus _agentBus;
        private readonly ITickProvider _tickProvider;
        private readonly ILogSink? _log;
        private readonly Action _requestFollowUp;
        private readonly Action _resetThinking;
        private readonly Action<AgentWorkflowPhase> _transitionWorkflow;
        private readonly Func<AgentDecision, Result<Unit, RimMindError>> _executeDecision;
        private readonly Func<int> _getPawnId;
        private readonly IDecisionValidator _validator;
        private readonly IToolRegistry? _toolRegistry;
        private readonly IAgenticLoopService _loopService;

        public DecisionProcessor(
            IAgentControl agent,
            IAgentBus agentBus,
            ITickProvider tickProvider,
            Action requestFollowUp,
            Action resetThinking,
            Action<AgentWorkflowPhase> transitionWorkflow,
            Func<AgentDecision, Result<Unit, RimMindError>> executeDecision,
            Func<int> getPawnId,
            ILogSink? log = null,
            IDecisionValidator? validator = null,
            IToolRegistry? toolRegistry = null,
            IAgenticLoopService? loopService = null)
        {
            _agent = agent ?? throw new ArgumentNullException(nameof(agent));
            _agentBus = agentBus ?? throw new ArgumentNullException(nameof(agentBus));
            _tickProvider = tickProvider ?? throw new ArgumentNullException(nameof(tickProvider));
            _requestFollowUp = requestFollowUp ?? throw new ArgumentNullException(nameof(requestFollowUp));
            _resetThinking = resetThinking ?? throw new ArgumentNullException(nameof(resetThinking));
            _transitionWorkflow = transitionWorkflow ?? throw new ArgumentNullException(nameof(transitionWorkflow));
            _executeDecision = executeDecision ?? throw new ArgumentNullException(nameof(executeDecision));
            _getPawnId = getPawnId ?? throw new ArgumentNullException(nameof(getPawnId));
            _log = log;
            _validator = validator ?? new DecisionValidator();
            _toolRegistry = toolRegistry;
            _loopService = loopService ?? new AgenticLoopService();
        }

        public bool ProcessResult(
            Result<LlmResponse, RimMindError> result,
            LlmRequestContext? context,
            IThinkStrategy strategy,
            IReadOnlyList<ToolDefinition> availableTools,
            int toolCallRound)
        {
            if (!result.IsOk)
            {
                _resetThinking();
                _transitionWorkflow(AgentWorkflowPhase.Idle);
                _log?.Warning($"[RimMind.Thinker] action=AIRequestFailed npcId={_agent.NpcId} modeId={_agent.CurrentModeId.Value} error={result.Error}");
                _agentBus.Publish(new DecisionFailedEvent(
                    _agent.NpcId,
                    _getPawnId(),
                    "",
                    result.Error.Message));
                return true;
            }

            var response = result.Value;
            var toolCallResults = context?.ToolCallResults;

            var decision = strategy.ParseDecision(_agent, response, toolCallResults);
            if (!decision.IsOk)
            {
                _resetThinking();
                _transitionWorkflow(AgentWorkflowPhase.Idle);
                _log?.Warning($"[RimMind.Thinker] action=ParseFailed npcId={_agent.NpcId} modeId={_agent.CurrentModeId.Value} error={decision.Error}");
                _agentBus.Publish(new DecisionFailedEvent(
                    _agent.NpcId,
                    _getPawnId(),
                    "",
                    decision.Error.Message));
                return true;
            }

            var loopResult = _loopService.Evaluate(decision.Value, toolCallRound);
            if (loopResult.ShouldContinue
                && toolCallResults != null
                && toolCallResults.Count > 0)
            {
                _requestFollowUp();
                return false;
            }

            if (_toolRegistry != null)
            {
                var validation = _validator.Validate(decision.Value, _toolRegistry);
                if (!validation.IsValid)
                {
                    _log?.Warning($"[RimMind.DecisionProcessor] validation failed: {validation.Reason}");
                    _resetThinking();
                    _transitionWorkflow(AgentWorkflowPhase.Idle);
                    return true;
                }
            }

            // Final decision: execute, record, publish
            _resetThinking();
            _agent.LastThinkTick = _tickProvider.TicksGame;

            _transitionWorkflow(AgentWorkflowPhase.Acting);

            var execResult = _executeDecision(decision.Value);
            var execSuccess = execResult.IsOk;

            _transitionWorkflow(AgentWorkflowPhase.Recording);

            _agent.RecordBehavior(new BehaviorRecordDto
            {
                Action = decision.Value.ActionIntent,
                Reason = decision.Value.Reason,
                Success = execSuccess,
                Timestamp = _tickProvider.TicksGame
            });

            _agentBus.Publish(new DecisionEvent(
                _agent.NpcId,
                _getPawnId(),
                decision.Value.ActionIntent ?? "think",
                decision.Value.Reason ?? "",
                decision.Value.ActionIntent ?? ""));

            _transitionWorkflow(AgentWorkflowPhase.Idle);

            return true;
        }
    }
}
