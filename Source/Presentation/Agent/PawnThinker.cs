using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Async;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Interfaces.Agent.Modes;
using RimMind.Application.Common.Interfaces.Agent.Psychology;
using RimMind.Application.Common.Interfaces.Agent.Social;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Models;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Application.Common.Models.Tools;
using RimMind.Application.Features.Agent;
using RimMind.Application.Features.Agent.InnerVoice;
using ThinkContextEnricher = RimMind.Application.Features.Agent.ThinkContextEnricher;
using RimMind.Domain.Common;
using RimMind.Domain.Enums;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;
using RimMind.Presentation.Api;
using Verse;

namespace RimMind.Presentation.Agent
{
    public class PawnThinker : IPawnThinker
    {
        private const int DefaultThinkCooldownTicks = RimMindDefaults.ThinkCooldownTicks;
        private readonly IPawnAgentVerse _agent;
        private readonly IAgentBus _agentBus;
        private readonly IAgentTickSettings? _tickSettings;
        private readonly ProactiveBehaviorExecutor _proactiveExecutor;
        private readonly ThinkContextEnricher _contextEnricher;
        private readonly ILogSink? _log;
        private readonly IDecisionProcessor _decisionProcessor;
        private readonly InnerVoiceHandler? _innerVoiceHandler;
        private readonly IPsychologyWatcher? _psychologyWatcher;
        private readonly ITickProvider _tickProvider;
        private readonly ICompletionFence _completionFence;
        private int _lastThinkTick;
        private int ThinkCooldownTicks => _tickSettings?.ThinkCooldownTicks ?? DefaultThinkCooldownTicks;
        private volatile bool _thinking;
        private IReadOnlyList<PerceptionBufferEntry> _cachedPerceptions = Array.Empty<PerceptionBufferEntry>();
        private int _requestSentTick;
        private volatile bool _hasPendingCallback;
        private Result<LlmResponse, RimMindError> _pendingResult;
        private LlmRequestContext? _pendingContext;
        private IThinkStrategy? _pendingStrategy;
        private IReadOnlyList<ToolDefinition>? _pendingAvailableTools;
        private int _pendingToolCallRound;
        private string? _pendingTraceId;
        private ICompletionFence? _pendingCompletionFence;
        private CancellationToken _pendingGenerationToken;

        internal PawnThinker(
            IPawnAgentVerse agent,
            IAgentTickSettings tickSettings,
            IAgentBus agentBus,
            InnerVoiceHandler? innerVoiceHandler,
            IPsychologyWatcher? psychologyWatcher,
            ITickProvider tickProvider,
            IDreamGenerator? dreamGenerator,
            IDreamThoughtInjector? dreamThoughtInjector,
            ITraitEvolver traitEvolver,
            ILogSink? log,
            ICompletionFence completionFence)
        {
            _agent = agent ?? throw new ArgumentNullException(nameof(agent));
            _tickSettings = tickSettings;
            _agentBus = agentBus ?? throw new ArgumentNullException(nameof(agentBus));
            _innerVoiceHandler = innerVoiceHandler;
            _psychologyWatcher = psychologyWatcher;
            _tickProvider = tickProvider ?? throw new ArgumentNullException(nameof(tickProvider));
            _log = log;
            _completionFence = completionFence ?? throw new ArgumentNullException(nameof(completionFence));
            _proactiveExecutor = new ProactiveBehaviorExecutor(
                agentBus,
                dreamGenerator,
                dreamThoughtInjector,
                traitEvolver,
                log,
                _completionFence);
            _contextEnricher = new ThinkContextEnricher(
                _innerVoiceHandler,
                _psychologyWatcher);
            _decisionProcessor = new DecisionProcessor(
                agent, agentBus, _tickProvider,
                RequestFollowUpThink, ResetThinkingState,
                phase => agent.TransitionWorkflow(phase),
                decision => agent.ExecuteDecision(decision),
                () => agent.Pawn?.thingIDNumber ?? -1, log,
                loopService: new AgenticLoopService(tickSettings.MaxToolCallDepth));
        }

        public bool IsThinking => _thinking;
        public int LastThinkTick => _lastThinkTick;

        public bool ShouldThink()
        {
            if (_agent.State != AgentState.Active) return false;
            if (_thinking) return false;
            return Find.TickManager.TicksGame - _lastThinkTick >= ThinkCooldownTicks;
        }

        public void ResetThinking()
        {
            _thinking = false;
            _hasPendingCallback = false;
            _requestSentTick = 0;
            _cachedPerceptions = Array.Empty<PerceptionBufferEntry>();
            _pendingTraceId = null;
            _pendingCompletionFence = null;
            _pendingGenerationToken = default;
        }

        public void Tick()
        {
            if (_agent.State != AgentState.Active) return;
            if (_hasPendingCallback) { _hasPendingCallback = false; ProcessPendingCallback(); }
            if (_thinking && _requestSentTick > 0)
            {
                var elapsed = Find.TickManager.TicksGame - _requestSentTick;
                if (elapsed > RimMindDefaults.ThinkRequestTimeoutTicks)
                {
                    _log?.Warning($"[RimMind.Thinker] action=ThinkTimeout npcId={_agent.Identity.NpcId} modeId={_agent.CurrentModeId.Value} elapsed={elapsed}");
                    _thinking = false; _requestSentTick = 0;
                    _cachedPerceptions = Array.Empty<PerceptionBufferEntry>();
                }
            }
            if (_thinking) return;
            if (Find.TickManager.TicksGame - _lastThinkTick < ThinkCooldownTicks) return;
            _lastThinkTick = Find.TickManager.TicksGame;
            Think();
        }

        private void Think()
        {
            _thinking = true;
            try
            {
                var pawn = _agent.Pawn;
                if (pawn == null || pawn.Dead) { _thinking = false; return; }
                var entries = _agent.PerceptionBuffer.Flush();
                _cachedPerceptions = entries;
                var mode = _agent.CurrentMode;
                if (!mode.ShouldThink(_agent, entries)) { _thinking = false; return; }
                var pawnId = pawn.thingIDNumber;
                var voiceText = _contextEnricher.ConsumeInnerVoice(_agent.Identity.NpcId);
                _contextEnricher.CheckPsychology(_agent, pawnId);
                _proactiveExecutor.ExecuteProactiveExtensions(_agent, mode, pawnId);
                var strategy = mode.GetThinkStrategy();
                var allowedToolIds = mode.AllowedToolIds(RimMindAPI.Tools);
                var availableTools = RimMindAPI.Tools.GetAllDefinitions()
                    .Where(d => allowedToolIds.Contains(d.Id)).ToList();
                var envelope = strategy.BuildEnvelope(_agent, entries, availableTools);
                EnrichEnvelope(envelope, voiceText);
                _requestSentTick = Find.TickManager.TicksGame;
                SendThinkRequest(envelope, strategy, availableTools, 0);
            }
            catch (Exception ex)
            {
                _thinking = false;
                _log?.Error($"[RimMind.Thinker] action=UnexpectedError npcId={_agent.Identity.NpcId} modeId={_agent.CurrentModeId.Value} error={ex}");
            }
        }

        private void EnrichEnvelope(LlmRequestEnvelope envelope, string? voiceText)
        {
            _contextEnricher.EnrichEnvelope(envelope, _agent.Identity.NpcId, voiceText);
            var agentInfo = (IAgentInfo)_agent;
            var recentHistory = agentInfo.GetRecentHistory(10);
            var successRate = agentInfo.GetRecentSuccessRate(10);
            var historySection = _contextEnricher.FormatBehaviorHistory(recentHistory, successRate);
            if (!string.IsNullOrEmpty(historySection))
            {
                envelope.GameStateInfo ??= new GameStateInfo();
                envelope.GameStateInfo.AddSection("behavior_history", historySection);
            }
        }

        private void SendThinkRequest(LlmRequestEnvelope envelope, IThinkStrategy strategy, IReadOnlyList<ToolDefinition> availableTools, int toolCallRound)
        {
            if (_completionFence.CancellationToken.IsCancellationRequested)
            { ResetThinkingState(); return; }

            _pendingStrategy = strategy;
            _pendingAvailableTools = availableTools;
            _pendingToolCallRound = toolCallRound;
            _pendingTraceId = envelope.TraceId;
            var modeId = _agent.CurrentModeId;
            RimMindAPI.Request.Send(envelope, (result, ctx) =>
            {
                if (!_completionFence.TryAcceptCompletion()) return;
                _pendingResult = result;
                _pendingContext = ctx;
                _pendingCompletionFence = _completionFence;
                _pendingGenerationToken = _completionFence.CancellationToken;
                if (ctx != null) ctx.AgentModeId = modeId;
                _hasPendingCallback = true;
            });
        }

        private void ProcessPendingCallback()
        {
            var completionFence = _pendingCompletionFence;
            var accepted = completionFence?.TryAcceptCompletion() == true;
            if (!accepted || _pendingGenerationToken.IsCancellationRequested)
            {
                ResetThinkingState();
                _pendingCompletionFence = null;
                _pendingGenerationToken = default;
                return;
            }

            var traceScope = _pendingTraceId != null ? TraceContext.BeginScope(_pendingTraceId) : null;
            try
            {
                _decisionProcessor.ProcessResult(_pendingResult, _pendingContext, _pendingStrategy!, _pendingAvailableTools!, _pendingToolCallRound);
            }
            catch (Exception ex)
            {
                _thinking = false;
                _agent.TransitionWorkflow(AgentWorkflowPhase.Idle);
                _log?.Error($"[RimMind.Thinker] action=CallbackError npcId={_agent.Identity.NpcId} modeId={_agent.CurrentModeId.Value} error={ex}");
            }
            finally
            {
                traceScope?.Dispose();
                _pendingCompletionFence = null;
                _pendingGenerationToken = default;
            }
        }

        private void RequestFollowUpThink()
        {
            var strategy = _pendingStrategy!;
            var availableTools = _pendingAvailableTools!;
            var toolCallRound = _pendingToolCallRound;
            var followUpEnvelope = strategy.BuildEnvelope(_agent, _cachedPerceptions, availableTools);
            _contextEnricher.EnrichEnvelope(followUpEnvelope, _agent.Identity.NpcId, null);
            SendThinkRequest(followUpEnvelope, strategy, availableTools, toolCallRound + 1);
        }

        private void ResetThinkingState()
        {
            _thinking = false;
            _cachedPerceptions = Array.Empty<PerceptionBufferEntry>();
        }

        public void ForceThink() => _lastThinkTick = 0;

        internal void RestoreLastThinkTick(int tick) => _lastThinkTick = tick;
    }
}
