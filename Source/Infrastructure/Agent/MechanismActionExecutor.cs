using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Interfaces.Mechanisms;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Application.Common.Models.Mechanisms;
using RimMind.Application.Common.Models.Tools;
using RimMind.Application.Common.Threading;
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
        private readonly Func<IReadOnlyList<IToolArgumentAdapter>>? _argumentAdapterProvider;

        public MechanismActionExecutor(
            IGameMechanismRegistry registry,
            IToolRegistry? toolRegistry = null,
            Func<IReadOnlyList<IToolArgumentAdapter>>? argumentAdapterProvider = null)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _toolRegistry = toolRegistry;
            _argumentAdapterProvider = argumentAdapterProvider;
        }

        public Result<Unit, RimMindError> ExecuteDecision(AgentDecision decision, int pawnId)
        {
            if (decision == null)
                return Result<Unit, RimMindError>.Err(RimMindErrors.Internal("AgentDecision is null"));

            if (string.IsNullOrEmpty(decision.ActionIntent))
                return Result<Unit, RimMindError>.Err(RimMindErrors.Internal("AgentDecision.ActionIntent is empty"));

            if (string.Equals(decision.ActionIntent, "dialogue.free", StringComparison.OrdinalIgnoreCase))
                return Result<Unit, RimMindError>.Ok(Unit.Value);

            // 1. Try finding registered tool in IToolRegistry (which resolves canonical IDs and Manifest.Aliases)
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

        internal static string ResolveToolId(string actionIntent)
        {
            if (string.IsNullOrWhiteSpace(actionIntent)) return "";
            return actionIntent.Trim();
        }

        private Result<Unit, RimMindError> ExecuteTool(IToolHandler tool, AgentDecision decision, int pawnId)
        {
            try
            {
                string argsJson = AdaptToolArguments(tool, decision, pawnId);
                var toolArgs = new ToolCallArgs
                {
                    ToolCallId = string.IsNullOrEmpty(decision.ToolCallId) ? Guid.NewGuid().ToString("N") : decision.ToolCallId!,
                    ToolName = tool.Definition.Id,
                    ArgumentsJson = argsJson,
                    PawnId = pawnId > 0 ? pawnId : (int?)null,
                    Ct = CancellationToken.None
                };

                var execTask = tool.ExecuteAsync(toolArgs, CancellationToken.None);
                if (!SynchronousTaskGate.TryCompleteSynchronously(execTask, out var execResult))
                {
                    return Result<Unit, RimMindError>.Err(
                        RimMindErrors.Internal($"Tool '{tool.Definition.Id}' {SynchronousTaskGate.AsyncNotSupportedMessage}"));
                }
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

        private string AdaptToolArguments(IToolHandler tool, AgentDecision decision, int pawnId)
        {
            string toolId = tool.Definition.Id;
            JObject obj;
            if (!string.IsNullOrWhiteSpace(decision.Param))
            {
                try
                {
                    var parsed = JToken.Parse(decision.Param!);
                    obj = parsed as JObject ?? BuildObjectFromScalarParam(parsed.Type == JTokenType.String ? parsed.Value<string>() : parsed.ToString());
                }
                catch
                {
                    // Plain text Param (e.g. "Construction"): preserve it as def_name for adapters/tools.
                    obj = BuildObjectFromScalarParam(decision.Param);
                }
            }
            else
            {
                obj = new JObject();
            }

            if (pawnId > 0)
            {
                bool isSelfTargetedTool = tool.Definition?.Manifest?.IsSelfTargeted == true;

                // Only fill pawn_id when it is missing or unparseable. An explicit but non-resolving id must
                // reach the mechanism and fail there; silently redirecting it to the caller made target-style
                // writes (thought/inspiration/mental state) land on the wrong pawn while reporting success.
                bool shouldOverride = isSelfTargetedTool || !obj.ContainsKey("pawn_id");
                if (!shouldOverride)
                {
                    var rawId = obj["pawn_id"];
                    if (rawId?.Type == JTokenType.Integer)
                    {
                        shouldOverride = rawId.Value<int>() <= 0;
                    }
                    else if (rawId?.Type == JTokenType.String)
                    {
                        if (RimMind.Application.Features.Tools.PawnIdParser.TryParse(rawId.Value<string>(), out int parsedId) && parsedId > 0)
                            obj["pawn_id"] = parsedId;
                        else
                            shouldOverride = true;
                    }
                    else
                    {
                        shouldOverride = true;
                    }
                }

                if (shouldOverride)
                {
                    obj["pawn_id"] = pawnId;
                }
            }

            var adapters = GetArgumentAdapters();
            if (adapters != null)
            {
                for (int i = 0; i < adapters.Count; i++)
                {
                    var adapter = adapters[i];
                    if (adapter == null) continue;
                    if (adapter.CanAdapt(toolId) ||
                        (!string.IsNullOrWhiteSpace(decision.ActionIntent) && adapter.CanAdapt(decision.ActionIntent)))
                    {
                        obj = adapter.AdaptArguments(toolId, obj, decision, pawnId) ?? obj;
                    }
                }
            }

            return obj.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static JObject BuildObjectFromScalarParam(string? raw)
        {
            var obj = new JObject();
            var value = raw?.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(value)) return obj;

            obj["def_name"] = value;
            return obj;
        }

        private IReadOnlyList<IToolArgumentAdapter>? GetArgumentAdapters()
        {
            if (_argumentAdapterProvider != null)
                return _argumentAdapterProvider();

            try
            {
                return RimMind.Presentation.Api.RimMindAPI.Ext.Get<IToolArgumentAdapter>()?.All;
            }
            catch
            {
                return null;
            }
        }

        private static Result<bool, RimMindError> InvokeWriteOperation(
            IGameMechanism mechanism, MechanismWriteArgs args, MechanismOperationType preferredOp)
        {
            try
            {
                var selectedOp = MechanismWriteOperationSelector.Select(mechanism.SupportedOperations, preferredOp);
                if (selectedOp.HasValue)
                {
                    return ExecuteOperation(mechanism, args, selectedOp.Value);
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
            Task<Result<bool, RimMindError>>? task = opType switch
            {
                MechanismOperationType.Trigger => mechanism.ExecuteTriggerAsync(args, CancellationToken.None),
                MechanismOperationType.Set => mechanism.ExecuteSetAsync(args, CancellationToken.None),
                MechanismOperationType.Add => mechanism.ExecuteAddAsync(args, CancellationToken.None),
                MechanismOperationType.Toggle => mechanism.ExecuteToggleAsync(args, CancellationToken.None),
                MechanismOperationType.Remove => mechanism.ExecuteRemoveAsync(args, CancellationToken.None),
                _ => null,
            };

            if (task == null)
            {
                return Result<bool, RimMindError>.Err(
                    RimMindErrors.MechanismOperationNotSupported(mechanism.MechanismId, opType.ToString()));
            }

            if (!SynchronousTaskGate.TryCompleteSynchronously(task, out var result))
            {
                return Result<bool, RimMindError>.Err(
                    RimMindErrors.Internal($"Mechanism '{mechanism.MechanismId}' ({opType}) {SynchronousTaskGate.AsyncNotSupportedMessage}"));
            }

            return result;
        }
    }
}
