using System;
using System.Linq;
using System.Text;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Interfaces.Context;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Models.Mechanisms;
using RimMind.Application.Features.Agent;
using RimMind.Domain.Agent.Modes;
using RimMind.Domain.Enums;
using RimMind.Infrastructure.UI.AgentFlow;
using RimMind.Infrastructure.Verse;
using RimMind.Presentation.Runtime.Services;
using UnityEngine;
using Verse;

namespace RimMind.Infrastructure.UI
{
    internal enum AgentFlowScope
    {
        Pawn,
        Map,
        Colony,
        Global
    }

    public partial class Window_AgentFlowLab
    {
        private void DrawScopeSelector(ref float y, float w)
        {
            DrawLabel(ref y, w, "RimMind.UI.AgentFlowLab.Scope".Translate(), GameFont.Small);

            float x = Padding;
            DrawScopeButton(ref x, y, 110f, AgentFlowScope.Pawn, "RimMind.UI.AgentFlowLab.ScopePawn");
            DrawScopeButton(ref x, y, 110f, AgentFlowScope.Map, "RimMind.UI.AgentFlowLab.ScopeMap");
            DrawScopeButton(ref x, y, 110f, AgentFlowScope.Colony, "RimMind.UI.AgentFlowLab.ScopeColony");
            DrawScopeButton(ref x, y, 110f, AgentFlowScope.Global, "RimMind.UI.AgentFlowLab.ScopeGlobal");
            y += BtnHeight + Padding;

            if (_selectedScope != AgentFlowScope.Pawn)
            {
                GUI.color = new Color(0.7f, 0.9f, 1f);
                string scopeHint = "RimMind.UI.AgentFlowLab.ScopeHint".Translate(_selectedScope.ToString());
                Widgets.Label(new Rect(Padding, y, w - Padding * 2, Text.LineHeight * 2f), scopeHint);
                GUI.color = Color.white;
                y += Text.LineHeight * 2f + Padding;
            }

            y += SectionGap;
        }

        private void DrawScopeButton(ref float x, float y, float width, AgentFlowScope scope, string labelKey)
        {
            bool selected = _selectedScope == scope;
            GUI.color = selected ? new Color(0.45f, 0.85f, 1f) : Color.white;
            if (Widgets.ButtonText(new Rect(x, y, width, BtnHeight), labelKey.Translate()))
            {
                if (_selectedScope != scope)
                {
                    _selectedScope = scope;
                    _scopedAgent = null;
                    _agent = null;
                    InvalidateCurrentTarget();
                    _stepTracker.Reset();
                }
            }
            GUI.color = Color.white;
            x += width + 6f;
        }

        private bool DrawNonPawnScope(ref float y, float w)
        {
            if (_selectedScope == AgentFlowScope.Pawn)
                return false;

            if (_scopedAgent == null)
            {
                RuntimeServiceScope runtimeScope = RuntimeServiceHub.Shared.Capture();
                var manager = runtimeScope.GetOptional<IScopedAgentManager>();
                var agentBus = runtimeScope.GetOptional<IAgentBus>();
                if (manager != null && agentBus != null)
                {
                    string scopeType = _selectedScope.ToString();
                    string scopeId = ResolveScopeId(_selectedScope);
                    int? mapId = _selectedScope == AgentFlowScope.Map ? Find.CurrentMap?.Index : null;
                    _scopedAgent = manager.GetOrCreate(scopeType, scopeId, agentBus, mapId);
                    _agent = _scopedAgent;
                    _stepTracker.Set(FlowLabStep.CreateAgent, StepStatus.Completed);
                }
            }

            if (_scopedAgent != null)
            {
                GUI.color = new Color(0.4f, 1f, 0.4f);
                Widgets.Label(new Rect(Padding, y, w - Padding * 2, LineH),
                    string.Format(
                        "RimMind.UI.AgentFlowLab.ScopedAgentActive".Translate().ToString(),
                        _scopedAgent.ScopeType,
                        _scopedAgent.ScopeId,
                        _scopedAgent.State));
                GUI.color = Color.white;
            }
            else
            {
                GUI.color = new Color(1f, 0.5f, 0.4f);
                Widgets.Label(new Rect(Padding, y, w - Padding * 2, LineH),
                    "RimMind.UI.AgentFlowLab.ScopeUnsupported".Translate());
                GUI.color = Color.white;
            }
            y += LineH + SectionGap;
            return _scopedAgent == null;
        }

        private string ResolveScopeId(AgentFlowScope scope)
        {
            return scope switch
            {
                AgentFlowScope.Map => Find.CurrentMap?.ToString() ?? "no_map",
                AgentFlowScope.Colony => Find.World?.info?.name ?? "colony",
                AgentFlowScope.Global => "global",
                _ => "unknown"
            };
        }

        private void DrawPawnSelection(ref float y, float w)
        {
            DrawStepHeader(ref y, w, "RimMind.UI.AgentFlowLab.SelectedPawn", FlowLabStep.SelectTarget);

            if (DrawNonPawnScope(ref y, w))
                return;

            if (_selectedPawn != null && _selectedPawn.Destroyed)
            {
                _selectedPawn = null;
                InvalidateCurrentTarget();
            }

            if (_selectedPawn != null)
            {
                string name = _selectedPawn.Name?.ToStringShort ?? _selectedPawn.LabelShort;
                Widgets.Label(new Rect(Padding, y, w - Padding * 2 - 80f, LineH),
                    "RimMind.UI.AgentFlowLab.SelectedPawn".Translate(name));
                _stepTracker.Set(FlowLabStep.SelectTarget, StepStatus.Completed);
            }
            else
            {
                GUI.color = Color.grey;
                Widgets.Label(new Rect(Padding, y, w - Padding * 2 - 80f, LineH),
                    "RimMind.UI.AgentFlowLab.NoPawn".Translate());
                GUI.color = Color.white;
                _stepTracker.Set(FlowLabStep.SelectTarget, StepStatus.Pending);
            }

            Rect refreshBtn = new Rect(w - 80f, y, 74f, BtnHeight);
            if (Widgets.ButtonText(refreshBtn, "RimMind.UI.AgentFlowLab.Refresh".Translate()))
            {
                _selectedPawn = _initialPawn ?? Find.Selector.SingleSelectedThing as Pawn;
                _initialPawn = null;
                _agent = null;
                _lastSnapshot = null;
                _lastDecisionInfo = "";
                _mappedMechanismsInfo = "";
                _dryRunCompleted = false;
                _dryRunResult = "";
                _lastDecision = null;
                _lastWriteArgs = null;
                _lastOperationType = MechanismOperationType.Set;
                _parsedDecisionInfo = "";
                _validationInfo = "";
                InvalidateCurrentTarget();
                _stepTracker.Reset();
                if (_selectedPawn != null)
                    _stepTracker.Set(FlowLabStep.SelectTarget, StepStatus.Completed);
            }
            y += LineH + Padding;

            y += SectionGap;
        }

        private void DrawAgentLifecycle(ref float y, float w)
        {
            DrawStepHeader(ref y, w, "RimMind.UI.AgentFlowLab.AgentLifecycle", FlowLabStep.CreateAgent);

            if (DrawNonPawnScope(ref y, w))
                return;

            if (_selectedPawn != null)
            {
                var comp = CompPawnAgent.GetComp(_selectedPawn);
                RuntimeServiceScope runtimeScope = RuntimeServiceHub.Shared.Capture();
                _agent = comp?.ResolveCurrentAgent(runtimeScope);

                if (_agent != null)
                {
                    string stateStr = _agent.State.ToString();
                    GUI.color = new Color(0.4f, 1f, 0.4f);
                    Widgets.Label(new Rect(Padding, y, w - Padding * 2, LineH),
                        "RimMind.UI.AgentFlowLab.AgentExists".Translate(stateStr));
                    GUI.color = Color.white;
                    _stepTracker.Set(FlowLabStep.CreateAgent, StepStatus.Completed);
                }
                else
                {
                    GUI.color = new Color(1f, 0.5f, 0.4f);
                    Widgets.Label(new Rect(Padding, y, w - Padding * 2, LineH),
                        "RimMind.UI.AgentFlowLab.AgentMissing".Translate());
                    GUI.color = Color.white;
                    _stepTracker.Set(FlowLabStep.CreateAgent, StepStatus.Pending);
                }
            }
            else
            {
                GUI.color = Color.grey;
                Widgets.Label(new Rect(Padding, y, w - Padding * 2, LineH),
                    "RimMind.UI.AgentFlowLab.NoPawn".Translate());
                GUI.color = Color.white;
            }
            y += LineH;

            Rect createBtn = new Rect(Padding, y, 160f, BtnHeight);
            if (Widgets.ButtonText(createBtn, "RimMind.UI.AgentFlowLab.CreateAgent".Translate()))
            {
                if (_selectedPawn != null)
                {
                    try
                    {
                        _stepTracker.Set(FlowLabStep.CreateAgent, StepStatus.Active);
                        var comp = CompPawnAgent.GetComp(_selectedPawn);
                        RuntimeServiceScope runtimeScope = RuntimeServiceHub.Shared.Capture();
                        IAgentControl? currentAgent = comp?.ResolveCurrentAgent(runtimeScope);
                        if (currentAgent != null)
                        {
                            _agent = currentAgent;
                            _stepTracker.Set(FlowLabStep.CreateAgent, StepStatus.Completed);
                        }
                        else
                        {
                            _lastError = "IPawnAgentFactoryVerse or IAgentBus not available";
                            _stepTracker.Set(FlowLabStep.CreateAgent, StepStatus.Failed);
                        }
                    }
                    catch (Exception ex)
                    {
                        _lastError = $"CreateAgent: {ex.Message}";
                        _stepTracker.Set(FlowLabStep.CreateAgent, StepStatus.Failed);
                    }
                }
            }
            y += BtnHeight + Padding;

            y += SectionGap;
        }

        private void DrawContextBuilding(ref float y, float w)
        {
            CompleteContextBuild();
            DrawStepHeader(ref y, w, "RimMind.UI.AgentFlowLab.ContextBuilding", FlowLabStep.BuildContext);

            if (DrawNonPawnScope(ref y, w))
                return;

            Rect buildBtn = new Rect(Padding, y, 180f, BtnHeight);
            if (Widgets.ButtonText(buildBtn, "RimMind.UI.AgentFlowLab.BuildContext".Translate()))
            {
                if (_selectedPawn != null)
                {
                    try
                    {
                        _stepTracker.Set(FlowLabStep.BuildContext, StepStatus.Active);
                        RuntimeServiceScope runtimeScope = RuntimeServiceHub.Shared.Capture();
                        var contextEngine = runtimeScope.GetOptional<IContextEngine>() as IContextBuilder;
                        if (contextEngine != null)
                        {
                            string npcId = $"NPC-{_selectedPawn.thingIDNumber}";
                            _lastSnapshot = null;
                            _lastError = string.Empty;
                            _asyncCoordinator.BeginContextBuild(
                                contextEngine.BuildSnapshotFromEnvelopeAsync(npcId, "[AgentFlowLab] Build context"),
                                runtimeScope.Token,
                                _targetGeneration);
                        }
                        else
                        {
                            _lastError = "IContextBuilder not available";
                            _stepTracker.Set(FlowLabStep.BuildContext, StepStatus.Failed);
                        }
                    }
                    catch (Exception ex)
                    {
                        _lastError = $"BuildContext: {ex.Message}";
                        _stepTracker.Set(FlowLabStep.BuildContext, StepStatus.Failed);
                    }
                }
            }
            y += BtnHeight + Padding;

            if (_lastSnapshot != null)
            {
                Widgets.Label(new Rect(Padding, y, w - Padding * 2, LineH),
                    "RimMind.UI.AgentFlowLab.TokenCount".Translate(_lastSnapshot.EstimatedTokens.ToString()));
                y += LineH;

                int msgCount = _lastSnapshot.Messages?.Count ?? 0;
                Widgets.Label(new Rect(Padding, y, w - Padding * 2, LineH),
                    "RimMind.UI.AgentFlowLab.Messages".Translate(msgCount.ToString()));
                y += LineH;

                if (msgCount > 0)
                {
                    var sb = new StringBuilder();
                    foreach (var msg in _lastSnapshot.Messages.Take(8))
                    {
                        int len = msg.Content?.Length ?? 0;
                        sb.AppendLine($"[{msg.Role}] {len}ch");
                    }
                    if (msgCount > 8)
                        sb.AppendLine($"... +{msgCount - 8} more");
                    float summaryH = Text.CalcHeight(sb.ToString(), w - Padding * 2);
                    GUI.color = new Color(0.7f, 0.7f, 0.7f);
                    Widgets.Label(new Rect(Padding, y, w - Padding * 2, summaryH), sb.ToString());
                    GUI.color = Color.white;
                    y += summaryH + Padding;
                }
            }
            else
            {
                GUI.color = Color.grey;
                Widgets.Label(new Rect(Padding, y, w - Padding * 2, LineH),
                    "RimMind.UI.AgentFlowLab.NoData".Translate());
                GUI.color = Color.white;
                y += LineH;
            }

            y += SectionGap;
        }

        private void CompleteContextBuild()
        {
            if (!_asyncCoordinator.PollContextBuild(_targetGeneration, out var snapshot, out var error))
                return;

            if (!string.IsNullOrEmpty(error))
            {
                _lastError = $"BuildContext: {LocalizeAsyncError(error)}";
                _stepTracker.Set(FlowLabStep.BuildContext, StepStatus.Failed);
                return;
            }

            _lastSnapshot = snapshot;
            if (snapshot == null)
            {
                _lastError = "BuildContext: no snapshot returned";
                _stepTracker.Set(FlowLabStep.BuildContext, StepStatus.Failed);
                return;
            }

            _generationState.MarkDerivedState();
            _stepTracker.Set(FlowLabStep.BuildContext, StepStatus.Completed);
        }

        private void InvalidateCurrentTarget()
        {
            _targetGeneration++;
            _asyncCoordinator.ResetContextBuild();
        }

        private void RefreshGenerationState()
        {
            RuntimeGenerationToken runtimeToken = RuntimeServiceHub.Shared.Capture().Token;
            if (!_generationState.Refresh(runtimeToken, _targetGeneration))
                return;

            _agent = null;
            _scopedAgent = null;
            _lastSnapshot = null;
            _lastDecision = null;
            _lastWriteArgs = null;
            _lastDecisionInfo = "";
            _mappedMechanismsInfo = "";
            _parsedDecisionInfo = "";
            _validationInfo = "";
            _dryRunCompleted = false;
            _dryRunResult = "";
            _requestStatus = "";
            _liveRequestToken = null;
            _liveRequestTargetGeneration = null;
            _asyncCoordinator.ResetAll();
            _stepTracker.Reset();
        }

        private string GetCurrentTargetId()
        {
            if (_selectedScope == AgentFlowScope.Pawn)
                return _selectedPawn == null ? "no_pawn" : $"NPC-{_selectedPawn.thingIDNumber}";

            return _scopedAgent == null
                ? ResolveScopeId(_selectedScope)
                : $"{_scopedAgent.ScopeType}:{_scopedAgent.ScopeId}";
        }
    }
}
