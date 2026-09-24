using System;
using System.Text;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Features.Agent.Modes;
using RimMind.Application.Features.Llm;
using RimMind.Domain.Agent.Modes;
using RimMind.Domain.Enums;
using RimMind.Domain.Llm;
using RimMind.Infrastructure.UI.AgentFlow;
using RimMind.Presentation.Api;
using RimMind.Presentation.Runtime.Services;
using UnityEngine;
using Verse;

namespace RimMind.Infrastructure.UI
{
    public partial class Window_AgentFlowLab
    {
        private void DrawLlmRequest(ref float y, float w)
        {
            DrawStepHeader(ref y, w, "RimMind.UI.AgentFlowLab.LlmRequest", FlowLabStep.SendRequest);

            if (DrawNonPawnScope(ref y, w))
                return;

            Rect sendBtn = new Rect(Padding, y, 180f, BtnHeight);
            bool wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && !_liveRequestToken.HasValue;
            bool sendClicked = Widgets.ButtonText(sendBtn, "RimMind.UI.AgentFlowLab.SendTestRequest".Translate());
            GUI.enabled = wasEnabled;
            if (sendClicked)
            {
                if (_selectedPawn != null)
                {
                    try
                    {
                        _stepTracker.Set(FlowLabStep.SendRequest, StepStatus.Active);
                        _dryRunCompleted = false;
                        _dryRunResult = "";
                        _lastDecision = null;
                        _lastWriteArgs = null;
                        _lastOperationType = MechanismOperationType.Set;
                        _parsedDecisionInfo = "";
                        _validationInfo = "";

                        if (_offlineMode)
                        {
                            HandleOfflineRequest();
                        }
                        else
                        {
                            HandleLiveRequest();
                        }
                    }
                    catch (Exception ex)
                    {
                        _liveRequestToken = null;
                        _requestStatus = "Failed";
                        _lastError = $"SendRequest: {ex.Message}";
                        _stepTracker.Set(FlowLabStep.SendRequest, StepStatus.Failed);
                    }
                }
            }
            y += BtnHeight + Padding;

            string statusDisplay = string.IsNullOrEmpty(_requestStatus)
                ? "RimMind.UI.AgentFlowLab.NoData".Translate()
                : _requestStatus;
            Widgets.Label(new Rect(Padding, y, w - Padding * 2, LineH),
                "RimMind.UI.AgentFlowLab.RequestStatus".Translate(statusDisplay));
            y += LineH;

            if (_offlineMode)
            {
                GUI.color = new Color(0.6f, 0.8f, 1f);
                Widgets.Label(new Rect(Padding, y, w - Padding * 2, LineH),
                    "RimMind.UI.AgentFlowLab.OfflineStubUsed".Translate());
                GUI.color = Color.white;
                y += LineH;
            }

            y += SectionGap;
        }

        private void HandleOfflineRequest()
        {
            _requestStatus = "Completed (Offline)";

            string stubResponse = "{\"action\":\"pawn.job.force_rest\",\"reason\":\"stub: offline test response\",\"param\":null}";
            _lastDecisionInfo = stubResponse;

            try
            {
                _lastDecision = new AgentDecision(
                    ActionIntent: "pawn.job.force_rest",
                    Reason: "stub: offline test response",
                    Param: null);
                _generationState.MarkDerivedState();
                _parsedDecisionInfo = FormatDecision(_lastDecision);
                _stepTracker.Set(FlowLabStep.SendRequest, StepStatus.Completed);
                _stepTracker.Set(FlowLabStep.ParseDecision, StepStatus.Completed);
                AutoDryRun();
            }
            catch (Exception ex)
            {
                _lastError = $"Offline parse: {ex.Message}";
                _stepTracker.Set(FlowLabStep.SendRequest, StepStatus.Completed);
                _stepTracker.Set(FlowLabStep.ParseDecision, StepStatus.Failed);
            }
        }

        private void HandleLiveRequest()
        {
            RuntimeServiceScope runtimeScope = RuntimeServiceHub.Shared.Capture();
            var settings = runtimeScope.GetOptional<ISettingsProvider>();
            if (settings == null || !settings.IsConfigured)
            {
                _requestStatus = "NotConfigured";
                _lastError = "API not configured";
                _stepTracker.Set(FlowLabStep.SendRequest, StepStatus.Failed);
                return;
            }

            RuntimeGenerationToken runtimeToken = runtimeScope.Token;
            int targetGeneration = _targetGeneration;
            _liveRequestToken = runtimeToken;
            _liveRequestTargetGeneration = targetGeneration;
            _requestStatus = "Pending";

            string npcId = $"NPC-{_selectedPawn!.thingIDNumber}";
            var envelope = LlmRequestEnvelopeBuilder
                .ForScenario("AgentFlowLab")
                .WithModId("AgentFlowLab")
                .WithNpcId(npcId)
                .WithMaxTokens(200)
                .WithTemperature(0f)
                .WithPriority(AIRequestPriority.High)
                .Build();

            envelope.Messages.Add(new ChatMessage { Role = "system", Content = "You are a test assistant. Reply with one actionable <Action> JSON block." });
            envelope.Messages.Add(new ChatMessage { Role = "user", Content = "Reply exactly like this: <Action>{\"action\":\"pawn.job.force_rest\",\"reason\":\"live test response\",\"param\":null}</Action>" });

            RimMindAPI.Request.Send(envelope, result =>
            {
                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    if (!TryAcceptLiveRequest(runtimeToken, targetGeneration))
                        return;
                    _liveRequestToken = null;
                    _liveRequestTargetGeneration = null;
                    if (result.IsOk)
                    {
                        _requestStatus = "Completed";
                        _lastDecisionInfo = result.Value.Content ?? "(empty)";
                        _stepTracker.Set(FlowLabStep.SendRequest, StepStatus.Completed);

                        try
                        {
                            var parseResult = ThinkStrategyHelper.ParseDecisionCore(new LlmResponse
                            {
                                Content = _lastDecisionInfo
                            });

                            if (parseResult.IsOk)
                            {
                                _lastDecision = parseResult.Value;
                                _generationState.MarkDerivedState();
                                _parsedDecisionInfo = FormatDecision(_lastDecision);
                                _stepTracker.Set(FlowLabStep.ParseDecision, StepStatus.Completed);
                                AutoDryRun();
                            }
                            else
                            {
                                _parsedDecisionInfo = parseResult.Error.Message;
                                _stepTracker.Set(FlowLabStep.ParseDecision, StepStatus.Failed);
                            }
                        }
                        catch (Exception ex)
                        {
                            _lastError = $"Parse decision: {ex.Message}";
                            _stepTracker.Set(FlowLabStep.ParseDecision, StepStatus.Failed);
                        }
                    }
                    else
                    {
                        _requestStatus = "Failed";
                        _lastError = result.Error.Message;
                        _stepTracker.Set(FlowLabStep.SendRequest, StepStatus.Failed);
                    }
                });
            });
        }

        private void CompleteStaleLiveRequest()
        {
            RuntimeGenerationToken? token = _liveRequestToken;
            bool staleRuntime = token.HasValue && !RuntimeServiceHub.Shared.IsCurrent(token.Value);
            bool staleTarget = _liveRequestTargetGeneration.HasValue
                && _liveRequestTargetGeneration.Value != _targetGeneration;
            if (!token.HasValue || (!staleRuntime && !staleTarget))
                return;

            _liveRequestToken = null;
            _liveRequestTargetGeneration = null;
            RuntimeServiceHub.Shared.RecordStaleCompletion(LifecycleEventSources.AgentFlowLab);
            _requestStatus = "RimMind.UI.Lifecycle.StaleCompletion".Translate();
            _lastError = _requestStatus;
            _stepTracker.Set(FlowLabStep.SendRequest, StepStatus.Failed);
        }

        private bool TryAcceptLiveRequest(RuntimeGenerationToken token, int targetGeneration)
        {
            if (!_liveRequestToken.HasValue || _liveRequestToken.Value != token)
                return false;
            if (_liveRequestTargetGeneration == targetGeneration
                && _generationState.CanPublish(
                    token,
                    targetGeneration,
                    RuntimeServiceHub.Shared.IsCurrent))
                return true;

            CompleteStaleLiveRequest();
            return false;
        }

        private void DrawDecisionParsing(ref float y, float w)
        {
            DrawStepHeader(ref y, w, "RimMind.UI.AgentFlowLab.DecisionParsing", FlowLabStep.ParseDecision);

            if (!string.IsNullOrEmpty(_parsedDecisionInfo))
            {
                float h = Text.CalcHeight(_parsedDecisionInfo, w - Padding * 2);
                h = Mathf.Min(h, 60f);
                GUI.color = new Color(0.7f, 0.7f, 0.7f);
                Widgets.Label(new Rect(Padding, y, w - Padding * 2, h), _parsedDecisionInfo);
                GUI.color = Color.white;
                y += h + Padding;
            }
            else if (!string.IsNullOrEmpty(_lastDecisionInfo))
            {
                float h = Text.CalcHeight(_lastDecisionInfo, w - Padding * 2);
                h = Mathf.Min(h, 60f);
                GUI.color = new Color(0.7f, 0.7f, 0.7f);
                Widgets.Label(new Rect(Padding, y, w - Padding * 2, h), _lastDecisionInfo);
                GUI.color = Color.white;
                y += h + Padding;
            }
            else
            {
                GUI.color = Color.grey;
                Widgets.Label(new Rect(Padding, y, w - Padding * 2, LineH),
                    "RimMind.UI.AgentFlowLab.NoData".Translate());
                GUI.color = Color.white;
                y += LineH;
            }

            if (!string.IsNullOrEmpty(_validationInfo))
            {
                GUI.color = new Color(1f, 0.7f, 0.3f);
                Widgets.Label(new Rect(Padding, y, w - Padding * 2, LineH),
                    "RimMind.UI.AgentFlowLab.ValidationResult".Translate(_validationInfo));
                GUI.color = Color.white;
                y += LineH;
            }

            y += SectionGap;
        }

        private static string FormatDecision(AgentDecision decision)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"ActionIntent: {decision.ActionIntent}");
            sb.AppendLine($"Reason: {decision.Reason}");
            if (!string.IsNullOrEmpty(decision.TargetPawnId))
                sb.AppendLine($"TargetPawnId: {decision.TargetPawnId}");
            if (!string.IsNullOrEmpty(decision.Param))
                sb.AppendLine($"Param: {decision.Param}");
            return sb.ToString();
        }
    }
}
