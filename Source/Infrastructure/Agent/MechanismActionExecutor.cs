using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Interfaces.Mechanisms;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Application.Common.Models.Mechanisms;
using RimMind.Application.Common.Models.Tools;
using RimMind.Application.Features.Agent;
using RimMind.Domain.Agent.Modes;
using RimMind.Domain.Common;
using RimMind.Domain.Enums;
using RimMind.Domain.ValueObjects;

namespace RimMind.Infrastructure.Agent
{
    /// <summary>
    /// Implements IActionExecutor by dispatching AgentDecision either to registered tools (IToolRegistry)
    /// or mapping to IGameMechanism write operations (IGameMechanismRegistry).
    /// </summary>
    public sealed class MechanismActionExecutor : IActionExecutor
    {
        private readonly IGameMechanismRegistry _registry;
        private readonly IToolRegistry? _toolRegistry;

        public MechanismActionExecutor(IGameMechanismRegistry registry, IToolRegistry? toolRegistry = null)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _toolRegistry = toolRegistry;
        }

        public Result<Unit, RimMindError> ExecuteDecision(AgentDecision decision, int pawnId)
        {
            if (decision == null)
                return Result<Unit, RimMindError>.Err(RimMindErrors.Internal("AgentDecision is null"));

            if (string.IsNullOrEmpty(decision.ActionIntent))
                return Result<Unit, RimMindError>.Err(RimMindErrors.Internal("AgentDecision.ActionIntent is empty"));

            if (string.Equals(decision.ActionIntent, "dialogue.free", StringComparison.OrdinalIgnoreCase))
                return Result<Unit, RimMindError>.Ok(Unit.Value);

            // 1. Try finding registered tool in IToolRegistry
            var toolId = ResolveToolId(decision.ActionIntent);
            var tool = _toolRegistry?.FindById(toolId);
            if (tool != null)
            {
                return ExecuteTool(tool, decision, pawnId);
            }

            // 2. Map to Mechanism write operations
            var (mechanismId, action) = DecisionMapper.ParseActionIntent(decision.ActionIntent);

            var mechanism = _registry.FindById(mechanismId);
            if (mechanism == null)
                return Result<Unit, RimMindError>.Err(RimMindErrors.ToolNotFound(mechanismId));

            var writeArgs = DecisionMapper.ToWriteArgs(decision, pawnId);

            // Determine which write operation to invoke based on the mechanism's supported operations
            // and the action type. Use action-semantic inference for preferred operation type.
            var preferredOp = DecisionMapper.InferOperationType(action);
            var result = InvokeWriteOperation(mechanism, writeArgs, preferredOp);

            return result.IsOk
                ? Result<Unit, RimMindError>.Ok(Unit.Value)
                : Result<Unit, RimMindError>.Err(result.Error);
        }

        public bool CanExecute(string actionIntent)
        {
            if (string.IsNullOrEmpty(actionIntent)) return false;
            if (string.Equals(actionIntent, "dialogue.free", StringComparison.OrdinalIgnoreCase)) return true;

            var toolId = ResolveToolId(actionIntent);
            if (_toolRegistry?.FindById(toolId) != null)
                return true;

            var (mechanismId, _) = DecisionMapper.ParseActionIntent(actionIntent);
            return _registry.FindById(mechanismId) != null;
        }

        private static string ResolveToolId(string actionIntent)
        {
            if (string.IsNullOrEmpty(actionIntent)) return "";
            if (string.Equals(actionIntent, "designate_map_order", StringComparison.OrdinalIgnoreCase))
                return "actions_designate_resources";
            if (string.Equals(actionIntent, "place_blueprint", StringComparison.OrdinalIgnoreCase))
                return "actions_place_blueprint";
            if (string.Equals(actionIntent, "add_worktable_bill", StringComparison.OrdinalIgnoreCase))
                return "actions_add_worktable_bill";
            if (string.Equals(actionIntent, "manage_equipment", StringComparison.OrdinalIgnoreCase))
                return "actions_manage_equipment";
            if (string.Equals(actionIntent, "manage_zone", StringComparison.OrdinalIgnoreCase))
                return "actions_manage_zone";
            if (string.Equals(actionIntent, "plan_room_project", StringComparison.OrdinalIgnoreCase))
                return "actions_plan_room_project";
            if (string.Equals(actionIntent, "query_buildable_structures", StringComparison.OrdinalIgnoreCase))
                return "actions_query_buildable_structures";
            if (string.Equals(actionIntent, "eat_and_recreation", StringComparison.OrdinalIgnoreCase))
                return "actions.eat_and_recreation";
            if (string.Equals(actionIntent, "stabilize_rest", StringComparison.OrdinalIgnoreCase))
                return "actions.stabilize_rest";
            if (string.Equals(actionIntent, "emergency_flee", StringComparison.OrdinalIgnoreCase))
                return "actions.emergency_flee";
            if (string.Equals(actionIntent, "triage_patient", StringComparison.OrdinalIgnoreCase))
                return "actions.triage_patient";
            return actionIntent;
        }

        private static Result<Unit, RimMindError> ExecuteTool(IToolHandler tool, AgentDecision decision, int pawnId)
        {
            try
            {
                string argsJson = AdaptToolArguments(tool.Definition.Id, decision, pawnId);
                var toolArgs = new ToolCallArgs
                {
                    ToolCallId = string.IsNullOrEmpty(decision.ToolCallId) ? Guid.NewGuid().ToString("N") : decision.ToolCallId,
                    ToolName = tool.Definition.Id,
                    ArgumentsJson = argsJson,
                    PawnId = pawnId > 0 ? pawnId : (int?)null,
                    Ct = CancellationToken.None
                };

                var execResult = tool.ExecuteAsync(toolArgs, CancellationToken.None).GetAwaiter().GetResult();
                if (execResult.IsOk)
                {
                    if (execResult.Value.IsError)
                    {
                        return Result<Unit, RimMindError>.Err(
                            RimMindErrors.InvalidAction(tool.Definition.Id, execResult.Value.Content ?? "Tool execution returned error"));
                    }
                    return Result<Unit, RimMindError>.Ok(Unit.Value);
                }
                return Result<Unit, RimMindError>.Err(execResult.Error);
            }
            catch (Exception ex)
            {
                return Result<Unit, RimMindError>.Err(RimMindErrors.Internal($"Tool '{tool.Definition.Id}' execution failed: {ex.Message}", ex));
            }
        }

        private static string AdaptToolArguments(string toolId, AgentDecision decision, int pawnId)
        {
            JObject obj;
            if (!string.IsNullOrWhiteSpace(decision.Param))
            {
                try
                {
                    var parsed = JToken.Parse(decision.Param!);
                    obj = parsed as JObject ?? new JObject();
                }
                catch
                {
                    obj = new JObject();
                }
            }
            else
            {
                obj = new JObject();
            }

            if (pawnId > 0)
            {
                bool isSelfTargetedTool =
                    toolId == "actions_stabilize_rest" ||
                    toolId == "actions.stabilize_rest" ||
                    toolId == "stabilize_rest" ||
                    toolId == "actions_eat_and_recreation" ||
                    toolId == "actions.eat_and_recreation" ||
                    toolId == "eat_and_recreation" ||
                    toolId == "actions_emergency_flee" ||
                    toolId == "actions.emergency_flee" ||
                    toolId == "emergency_flee" ||
                    toolId == "prioritize_work" ||
                    toolId == "take_job";

                bool shouldOverride = isSelfTargetedTool || !obj.ContainsKey("pawn_id") || obj["pawn_id"]?.Type == JTokenType.String;
                if (!shouldOverride && obj["pawn_id"]?.Type == JTokenType.Integer)
                {
                    int existingId = obj["pawn_id"]!.Value<int>();
                    if (existingId <= 0 || RimMind.Presentation.Api.RimMindPawnLookup.FindPawnByNumber(existingId) == null)
                    {
                        shouldOverride = true;
                    }
                }

                if (shouldOverride)
                {
                    obj["pawn_id"] = pawnId;
                }
            }

            if (toolId == "actions_designate_resources")
            {
                string targetStr = (obj["target"]?.ToString() ?? obj["target_name"]?.ToString() ?? "").Trim();
                bool isFoodOrCropTarget =
                    targetStr.IndexOf("berr", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    targetStr.IndexOf("food", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    targetStr.IndexOf("edible", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    targetStr.IndexOf("crop", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    targetStr.IndexOf("rice", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    targetStr.IndexOf("potato", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    targetStr.IndexOf("corn", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    targetStr.IndexOf("healroot", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    targetStr.IndexOf("agave", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    targetStr.IndexOf("ambrosia", StringComparison.OrdinalIgnoreCase) >= 0;

                if (obj.TryGetValue("order_type", StringComparison.OrdinalIgnoreCase, out var otVal))
                {
                    string ot = otVal.ToString();
                    if (ot.Equals("CutPlants", StringComparison.OrdinalIgnoreCase))
                        obj["designation_type"] = isFoodOrCropTarget ? "Harvest" : "ChopWood";
                    else if (ot.Equals("ChopWood", StringComparison.OrdinalIgnoreCase))
                        obj["designation_type"] = "ChopWood";
                    else if (ot.Equals("Harvest", StringComparison.OrdinalIgnoreCase))
                        obj["designation_type"] = "Harvest";
                    else if (ot.Equals("Mine", StringComparison.OrdinalIgnoreCase))
                        obj["designation_type"] = "Mine";
                    else
                        obj["designation_type"] = ot;
                }
                else if (!obj.ContainsKey("designation_type"))
                {
                    obj["designation_type"] = isFoodOrCropTarget ? "Harvest" : "ChopWood";
                }
            }
            else if (toolId == "actions.eat_and_recreation" || toolId == "actions.stabilize_rest" ||
                     toolId == "eat_and_recreation" || toolId == "stabilize_rest")
            {
                if (!obj.ContainsKey("reason"))
                {
                    obj["reason"] = !string.IsNullOrEmpty(decision.Reason) ? decision.Reason : "autonomous recovery";
                }
            }

            return obj.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static Result<bool, RimMindError> InvokeWriteOperation(
            IGameMechanism mechanism, MechanismWriteArgs args, MechanismOperationType preferredOp)
        {
            var supportedOps = mechanism.SupportedOperations;

            try
            {
                // Try preferred operation first if supported
                if (supportedOps.Contains(preferredOp))
                {
                    return ExecuteOperation(mechanism, args, preferredOp);
                }

                // Fallback: Trigger is the primary operation for agent-initiated actions
                if (supportedOps.Contains(MechanismOperationType.Trigger))
                {
                    return ExecuteOperation(mechanism, args, MechanismOperationType.Trigger);
                }

                // Set for state-changing operations
                if (supportedOps.Contains(MechanismOperationType.Set))
                {
                    return ExecuteOperation(mechanism, args, MechanismOperationType.Set);
                }

                // Add for additive operations
                if (supportedOps.Contains(MechanismOperationType.Add))
                {
                    return ExecuteOperation(mechanism, args, MechanismOperationType.Add);
                }

                // Toggle for boolean state operations
                if (supportedOps.Contains(MechanismOperationType.Toggle))
                {
                    return ExecuteOperation(mechanism, args, MechanismOperationType.Toggle);
                }

                // Remove for removal operations
                if (supportedOps.Contains(MechanismOperationType.Remove))
                {
                    return ExecuteOperation(mechanism, args, MechanismOperationType.Remove);
                }

                return Result<bool, RimMindError>.Err(
                    RimMindErrors.MechanismOperationNotSupported(
                        mechanism.MechanismId, "any write operation"));
            }
            catch (Exception ex)
            {
                return Result<bool, RimMindError>.Err(
                    RimMindErrors.Internal($"Mechanism write failed: {ex.Message}", ex));
            }
        }

        private static Result<bool, RimMindError> ExecuteOperation(
            IGameMechanism mechanism, MechanismWriteArgs args, MechanismOperationType opType)
        {
            return opType switch
            {
                MechanismOperationType.Trigger => mechanism.ExecuteTriggerAsync(args, CancellationToken.None).GetAwaiter().GetResult(),
                MechanismOperationType.Set => mechanism.ExecuteSetAsync(args, CancellationToken.None).GetAwaiter().GetResult(),
                MechanismOperationType.Add => mechanism.ExecuteAddAsync(args, CancellationToken.None).GetAwaiter().GetResult(),
                MechanismOperationType.Toggle => mechanism.ExecuteToggleAsync(args, CancellationToken.None).GetAwaiter().GetResult(),
                MechanismOperationType.Remove => mechanism.ExecuteRemoveAsync(args, CancellationToken.None).GetAwaiter().GetResult(),
                _ => Result<bool, RimMindError>.Err(RimMindErrors.MechanismOperationNotSupported(mechanism.MechanismId, opType.ToString())),
            };
        }
    }
}
