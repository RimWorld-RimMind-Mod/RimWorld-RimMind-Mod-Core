using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Interfaces.Mechanisms;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Application.Common.Models;
using RimMind.Application.Common.Models.Mechanisms;
using RimMind.Application.Features.Agent;
using RimMind.Domain.Enums;
using RimMind.Domain.ValueObjects;
using RimMind.Infrastructure.UI.AgentFlow;
using RimMind.Presentation.Runtime.Services;
using UnityEngine;
using Verse;

namespace RimMind.Infrastructure.UI
{
    public partial class Window_AgentFlowLab
    {
        private void DrawMechanismMapping(ref float y, float w)
        {
            DrawStepHeader(ref y, w, "RimMind.UI.AgentFlowLab.MechanismMapping", FlowLabStep.MapMechanism);

            if (DrawNonPawnScope(ref y, w))
                return;

            Rect dryRunBtn = new Rect(Padding, y, 200f, BtnHeight);
            if (Widgets.ButtonText(dryRunBtn, "RimMind.UI.AgentFlowLab.DryRun".Translate()))
            {
                try
                {
                    PerformDryRun();
                }
                catch (Exception ex)
                {
                    _lastError = $"DryRun: {ex.Message}";
                    _stepTracker.Set(FlowLabStep.DryRun, StepStatus.Failed);
                }
            }
            y += BtnHeight + Padding;

            if (!string.IsNullOrEmpty(_dryRunResult))
            {
                float h = Text.CalcHeight(_dryRunResult, w - Padding * 2);
                h = Mathf.Min(h, 80f);
                GUI.color = new Color(0.7f, 0.7f, 0.7f);
                Widgets.Label(new Rect(Padding, y, w - Padding * 2, h), _dryRunResult);
                GUI.color = Color.white;
                y += h + Padding;
            }
            else if (!string.IsNullOrEmpty(_mappedMechanismsInfo))
            {
                float h = Text.CalcHeight(_mappedMechanismsInfo, w - Padding * 2);
                h = Mathf.Min(h, 80f);
                GUI.color = new Color(0.7f, 0.7f, 0.7f);
                Widgets.Label(new Rect(Padding, y, w - Padding * 2, h), _mappedMechanismsInfo);
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

            if (!_dryRunCompleted)
            {
                GUI.color = new Color(0.5f, 0.5f, 0.5f);
                Rect execBtn = new Rect(Padding, y, 220f, BtnHeight);
                Widgets.ButtonText(execBtn, "RimMind.UI.AgentFlowLab.ExecuteRequiresDryRun".Translate());
                GUI.color = Color.white;
            }
            else
            {
                Rect execBtn = new Rect(Padding, y, 220f, BtnHeight);
                GUI.color = new Color(1f, 0.6f, 0.4f);
                bool wasEnabled = GUI.enabled;
                GUI.enabled = !_asyncCoordinator.HasPendingMechanismExecutionForGeneration(_targetGeneration);
                if (Widgets.ButtonText(execBtn, "RimMind.UI.AgentFlowLab.ExecuteMechanism".Translate()))
                {
                    Find.WindowStack.Add(new Dialog_MessageBox(
                        "RimMind.UI.AgentFlowLab.ConfirmExecute".Translate(),
                        "Confirm".Translate(),
                        () =>
                        {
                            try
                            {
                                _stepTracker.Set(FlowLabStep.Execute, StepStatus.Active);
                                if (_lastWriteArgs == null)
                                {
                                    _lastError = "RimMind.UI.AgentFlowLab.ExecuteNoWriteArgs".Translate();
                                    _stepTracker.Set(FlowLabStep.Execute, StepStatus.Failed);
                                    return;
                                }

                                RuntimeServiceScope runtimeScope = RuntimeServiceHub.Shared.Capture();
                                var mechanismRegistry = runtimeScope.GetOptional<IGameMechanismRegistry>();
                                if (mechanismRegistry == null)
                                {
                                    _lastError = "RimMind.UI.AgentFlowLab.ExecuteNoRegistry".Translate();
                                    _stepTracker.Set(FlowLabStep.Execute, StepStatus.Failed);
                                    return;
                                }

                                var targetMech = mechanismRegistry.FindById(_lastWriteArgs.MechanismId);
                                if (targetMech != null)
                                {
                                    _asyncCoordinator.BeginMechanismExecution(
                                        ExecuteMappedMechanism(targetMech, _lastWriteArgs, _lastOperationType),
                                        new AgentFlowExecutionContext(
                                            runtimeScope.Token,
                                            _targetGeneration,
                                            _selectedScope.ToString(),
                                            GetCurrentTargetId(),
                                            targetMech.MechanismId,
                                            _lastOperationType),
                                        runtimeScope.Token);
                                }
                                else
                                {
                                    _lastError = $"Mechanism not found: {_lastWriteArgs.MechanismId}";
                                    _stepTracker.Set(FlowLabStep.Execute, StepStatus.Failed);
                                }
                            }
                            catch (Exception ex)
                            {
                                _lastError = $"ExecuteMechanism: {ex.Message}";
                                _stepTracker.Set(FlowLabStep.Execute, StepStatus.Failed);
                            }
                        },
                        "Cancel".Translate(),
                        null,
                        "RimMind.UI.AgentFlowLab.ExecuteMechanism".Translate()));
                }
                GUI.enabled = wasEnabled;
                GUI.color = Color.white;
            }
            y += BtnHeight + Padding;

            if (!string.IsNullOrEmpty(_validationInfo))
            {
                GUI.color = new Color(1f, 0.7f, 0.3f);
                float h = Text.CalcHeight(_validationInfo, w - Padding * 2);
                h = Mathf.Min(h, 40f);
                Widgets.Label(new Rect(Padding, y, w - Padding * 2, h), _validationInfo);
                GUI.color = Color.white;
                y += h + Padding;
            }

            y += SectionGap;
        }

        private void PerformDryRun()
        {
            _stepTracker.Set(FlowLabStep.DryRun, StepStatus.Active);
            _stepTracker.Set(FlowLabStep.MapMechanism, StepStatus.Active);
            _dryRunCompleted = false;
            _lastWriteArgs = null;

            RuntimeServiceScope runtimeScope = RuntimeServiceHub.Shared.Capture();
            var mechanismRegistry = runtimeScope.GetOptional<IGameMechanismRegistry>();
            var toolRegistry = runtimeScope.GetOptional<IToolRegistry>();
            var approvalGate = runtimeScope.GetOptional<IHumanApprovalGate>();

            if (mechanismRegistry == null)
            {
                _dryRunResult = "MechanismRegistry not available";
                _lastError = _dryRunResult;
                _lastWriteArgs = null;
                _lastOperationType = MechanismOperationType.Set;
                _stepTracker.Set(FlowLabStep.DryRun, StepStatus.Failed);
                _stepTracker.Set(FlowLabStep.MapMechanism, StepStatus.Failed);
                return;
            }

            var sb = new StringBuilder();
            var mechanisms = mechanismRegistry.All;
            sb.AppendLine("RimMind.UI.AgentFlowLab.DryRunHeader".Translate(mechanisms.Count.ToString()));

            foreach (var mech in mechanisms)
            {
                var writeActions = mech.GetWriteActions();
                string actionsStr = writeActions != null && writeActions.Count > 0
                    ? string.Join(", ", writeActions.Select(a => a.Action))
                    : "(read-only)";
                string toolMatch = "";
                if (toolRegistry != null)
                {
                    var match = toolRegistry.FindById(mech.MechanismId);
                    toolMatch = match != null ? $" -> tool:{match.Definition.Id}" : " -> no tool mapping";
                }

                string riskStr = mech.Risk.ToString();
                string approvalStr = "";
                if (approvalGate != null && _lastDecision != null)
                {
                    var riskLevel = mech.Risk switch
                    {
                        MechanismRisk.Safe => RiskLevel.Low,
                        MechanismRisk.Moderate => RiskLevel.Medium,
                        MechanismRisk.Dangerous => RiskLevel.High,
                        _ => RiskLevel.Low
                    };
                    bool needsApproval = approvalGate.RequiresApproval(_lastDecision, riskLevel);
                    approvalStr = needsApproval ? " [APPROVAL REQUIRED]" : "";
                }

                sb.AppendLine($"  {mech.MechanismId} [{mech.Scope}] risk={riskStr} actions={actionsStr}{toolMatch}{approvalStr}");
            }

            if (_lastDecision == null)
            {
                sb.AppendLine();
                sb.AppendLine("RimMind.UI.AgentFlowLab.DryRunNoDecision".Translate());
                _dryRunResult = sb.ToString();
                _lastError = "RimMind.UI.AgentFlowLab.DryRunNoDecision".Translate();
                _lastWriteArgs = null;
                _lastOperationType = MechanismOperationType.Set;
                _stepTracker.Set(FlowLabStep.DryRun, StepStatus.Failed);
                _stepTracker.Set(FlowLabStep.MapMechanism, StepStatus.Failed);
                return;
            }

            if (_lastDecision != null)
            {
                sb.AppendLine();
                sb.AppendLine("RimMind.UI.AgentFlowLab.DryRunDecision".Translate(_lastDecision.ActionIntent));
                var writeArgs = DecisionMapper.ToWriteArgs(_lastDecision, _selectedPawn?.thingIDNumber ?? 0);
                _lastOperationType = DecisionMapper.InferOperationType(writeArgs.Action);
                var targetMech = mechanismRegistry.FindById(writeArgs.MechanismId);
                if (targetMech != null)
                {
                    _lastWriteArgs = writeArgs;
                    sb.AppendLine("RimMind.UI.AgentFlowLab.DryRunTarget".Translate(
                        targetMech.MechanismId, targetMech.Risk.ToString()));
                    sb.AppendLine($"  operation={_lastOperationType} action={writeArgs.Action}");

                    if (targetMech.Risk == MechanismRisk.Dangerous)
                    {
                        sb.AppendLine("  *** DANGEROUS - Approval required before execution ***");
                    }
                }
                else
                {
                    string noTarget = "RimMind.UI.AgentFlowLab.DryRunNoTarget".Translate(writeArgs.MechanismId);
                    sb.AppendLine(noTarget);
                    _dryRunResult = sb.ToString();
                    _lastError = noTarget;
                    _lastWriteArgs = null;
                    _lastOperationType = MechanismOperationType.Set;
                    _stepTracker.Set(FlowLabStep.DryRun, StepStatus.Failed);
                    _stepTracker.Set(FlowLabStep.MapMechanism, StepStatus.Failed);
                    return;
                }

                if (toolRegistry != null)
                {
                    var validator = RuntimeServiceHub.Shared.Capture().GetOptional<IDecisionValidator>();
                    if (validator != null)
                    {
                        var validationResult = validator.Validate(_lastDecision, toolRegistry);
                        _validationInfo = validationResult.IsValid
                            ? "RimMind.UI.AgentFlowLab.ValidationPassed".Translate()
                            : "RimMind.UI.AgentFlowLab.ValidationFailed".Translate(validationResult.Reason);
                    }
                }
            }

            _dryRunResult = sb.ToString();
            _dryRunCompleted = true;
            _stepTracker.Set(FlowLabStep.DryRun, StepStatus.Completed);
            _stepTracker.Set(FlowLabStep.MapMechanism, StepStatus.Completed);
        }

        private static Task<Result<bool, RimMindError>> ExecuteMappedMechanism(
            IGameMechanism mechanism,
            MechanismWriteArgs args,
            MechanismOperationType operationType)
        {
            return operationType switch
            {
                MechanismOperationType.Add => mechanism.ExecuteAddAsync(args, default),
                MechanismOperationType.Remove => mechanism.ExecuteRemoveAsync(args, default),
                MechanismOperationType.Toggle => mechanism.ExecuteToggleAsync(args, default),
                MechanismOperationType.Trigger => mechanism.ExecuteTriggerAsync(args, default),
                MechanismOperationType.Watch => mechanism.ExecuteWatchAsync(args, default),
                _ => mechanism.ExecuteSetAsync(args, default),
            };
        }

        private void CompleteMechanismExecution()
        {
            if (!_asyncCoordinator.PollMechanismExecution(_targetGeneration, out var completion))
                return;

            var execution = completion!;
            if (execution.Context.TargetGeneration != _targetGeneration)
            {
                _lastError = $"{ "RimMind.UI.Lifecycle.StaleCompletion".Translate()} " +
                    $"({execution.Context.Scope}:{execution.Context.TargetId})";
                _stepTracker.Set(FlowLabStep.Execute, StepStatus.Failed);
                return;
            }

            if (!string.IsNullOrEmpty(execution.Error))
            {
                _lastError = $"ExecuteMechanism: {LocalizeAsyncError(execution.Error)}";
                _stepTracker.Set(FlowLabStep.Execute, StepStatus.Failed);
                return;
            }

            if (execution.Result!.Value.IsOk)
            {
                _lastError = $"Execute {execution.Context.Operation} ok: {execution.Result.Value.Value}";
                _stepTracker.Set(FlowLabStep.Execute, StepStatus.Completed);
                return;
            }

            _lastError = execution.Result.Value.Error.Message;
            _stepTracker.Set(FlowLabStep.Execute, StepStatus.Failed);
        }

        private static string LocalizeAsyncError(string error)
            => error == AgentFlowAsyncCoordinator.StaleCompletionTranslationKey
                ? "RimMind.UI.Lifecycle.StaleCompletion".Translate()
                : error;

        private void AutoDryRun()
        {
            try
            {
                PerformDryRun();
            }
            catch (Exception ex)
            {
                _lastError = $"AutoDryRun: {ex.Message}";
                _stepTracker.Set(FlowLabStep.DryRun, StepStatus.Failed);
            }
        }
    }
}
