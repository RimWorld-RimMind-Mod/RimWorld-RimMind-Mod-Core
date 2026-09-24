using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Interfaces.Mechanisms;
using RimMind.Application.Common.Models.Mechanisms;
using RimMind.Application.Features.Agent;
using RimMind.Domain.Agent.Modes;
using RimMind.Domain.Common;
using RimMind.Domain.Enums;
using RimMind.Domain.ValueObjects;

namespace RimMind.Infrastructure.Agent
{
    /// <summary>
    /// Implements IActionExecutor by mapping AgentDecision to IGameMechanism write operations.
    /// Finds the appropriate Mechanism via IGameMechanismRegistry, builds MechanismWriteArgs
    /// via DecisionMapper, and invokes the Mechanism's write method.
    /// </summary>
    public sealed class MechanismActionExecutor : IActionExecutor
    {
        private readonly IGameMechanismRegistry _registry;

        public MechanismActionExecutor(IGameMechanismRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public Result<Unit, RimMindError> ExecuteDecision(AgentDecision decision, int pawnId)
        {
            if (decision == null)
                return Result<Unit, RimMindError>.Err(RimMindErrors.Internal("AgentDecision is null"));

            if (string.IsNullOrEmpty(decision.ActionIntent))
                return Result<Unit, RimMindError>.Err(RimMindErrors.Internal("AgentDecision.ActionIntent is empty"));

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
            var (mechanismId, _) = DecisionMapper.ParseActionIntent(actionIntent);
            return _registry.FindById(mechanismId) != null;
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
