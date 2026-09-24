using System;
using System.Collections.Generic;

namespace RimMind.Domain.ValueObjects
{
    public static class RimMindErrors
    {
        public static RimMindError ClientNotConfigured(string source) => new(RimMindErrorCode.ClientNotConfigured, "AI client is not configured")
        {
            Source = source,
            TraceId = TraceContext.Current,
        };

        public static RimMindError ClientTransient(string message, Exception? inner = null) => new(RimMindErrorCode.ClientTransientFailure, message)
        {
            InnerException = inner,
            TraceId = TraceContext.Current,
        };

        public static RimMindError ClientPermanent(string message, Exception? inner = null) => new(RimMindErrorCode.ClientPermanentFailure, message)
        {
            InnerException = inner,
            TraceId = TraceContext.Current,
        };

        public static RimMindError CircuitOpen() => new(RimMindErrorCode.ClientCircuitOpen, "Circuit breaker is open")
        {
            TraceId = TraceContext.Current,
        };

        public static RimMindError ContextBuildFailed(string message, Exception? inner = null) => new(RimMindErrorCode.ContextBuildFailed, message)
        {
            InnerException = inner,
            TraceId = TraceContext.Current,
        };

        public static RimMindError PipelineShortCircuited(string reason) => new(RimMindErrorCode.PipelineShortCircuited, reason)
        {
            TraceId = TraceContext.Current,
        };

        public static RimMindError ToolNotFound(string toolId) => new(RimMindErrorCode.ToolNotFound, $"Tool '{toolId}' not registered")
        {
            Details = new Dictionary<string, object?> { ["tool_id"] = toolId },
            TraceId = TraceContext.Current,
        };

        public static RimMindError ToolExecution(string toolId, string message, Exception? inner = null) => new(RimMindErrorCode.ToolExecutionFailed, $"Tool '{toolId}' execution failed: {message}")
        {
            Details = new Dictionary<string, object?> { ["tool_id"] = toolId },
            InnerException = inner,
            TraceId = TraceContext.Current,
        };

        public static RimMindError ToolPolicyDenied(string toolId, string reason) => new(RimMindErrorCode.ToolPolicyDenied, $"Tool '{toolId}' denied by policy: {reason}")
        {
            Details = new Dictionary<string, object?> { ["tool_id"] = toolId },
            TraceId = TraceContext.Current,
        };

        public static RimMindError ToolMaxDepthExceeded(int depth) => new(RimMindErrorCode.ToolMaxDepthExceeded, $"Tool call recursion exceeded max depth of {depth}")
        {
            Details = new Dictionary<string, object?> { ["max_depth"] = depth },
            TraceId = TraceContext.Current,
        };

        public static RimMindError MechanismOperationNotSupported(string mechanismId, string operation) => new(RimMindErrorCode.MechanismOperationNotSupported, $"Mechanism '{mechanismId}' does not support operation '{operation}'")
        {
            Details = new Dictionary<string, object?> { ["mechanism_id"] = mechanismId, ["operation"] = operation },
            TraceId = TraceContext.Current,
        };

        public static RimMindError PawnNotFound(int pawnId) => new(RimMindErrorCode.MechanismPawnNotFound, $"Pawn with ID {pawnId} not found")
        {
            Details = new Dictionary<string, object?> { ["pawn_id"] = pawnId },
            TraceId = TraceContext.Current,
        };

        public static RimMindError InvalidDefName(string defName) => new(RimMindErrorCode.MechanismInvalidDefName, $"Invalid def name '{defName}'")
        {
            Details = new Dictionary<string, object?> { ["def_name"] = defName },
            TraceId = TraceContext.Current,
        };

        public static RimMindError MapNotFound(int mapId) => new(RimMindErrorCode.MechanismMapNotFound, $"Map with ID {mapId} not found")
        {
            Details = new Dictionary<string, object?> { ["map_id"] = mapId },
            TraceId = TraceContext.Current,
        };

        public static RimMindError InvalidAction(string mechanismId, string action) => new(RimMindErrorCode.MechanismInvalidAction, $"Invalid action '{action}' for mechanism '{mechanismId}'")
        {
            Details = new Dictionary<string, object?> { ["mechanism_id"] = mechanismId, ["action"] = action },
            TraceId = TraceContext.Current,
        };

        public static RimMindError NpcNotFound(string npcId) => new(RimMindErrorCode.NpcNotFound, $"NPC '{npcId}' not found")
        {
            Details = new Dictionary<string, object?> { ["npc_id"] = npcId },
            TraceId = TraceContext.Current,
        };

        public static RimMindError RemoteBackendFailed(string message, Exception? inner = null) => new(RimMindErrorCode.RemoteBackendFailed, message)
        {
            InnerException = inner,
            TraceId = TraceContext.Current,
        };

        public static RimMindError Cancelled() => new(RimMindErrorCode.Cancelled, "Operation cancelled")
        {
            TraceId = TraceContext.Current,
        };

        public static RimMindError Timeout(string message) => new(RimMindErrorCode.Timeout, message)
        {
            TraceId = TraceContext.Current,
        };

        public static RimMindError Internal(string message, Exception? inner = null) => new(RimMindErrorCode.InternalError, message)
        {
            InnerException = inner,
            TraceId = TraceContext.Current,
        };

        public static RimMindError NotImplemented(string message) => new(RimMindErrorCode.NotImplemented, message)
        {
            TraceId = TraceContext.Current,
        };

        /// <summary>
        /// Global hook for routing domain warnings to the game engine or test log sinks.
        /// </summary>
        public static Action<string, Exception?>? OnWarn { get; set; }

        /// <summary>
        /// Global hook for routing domain errors to the game engine or test log sinks.
        /// </summary>
        public static Action<string, Exception?>? OnError { get; set; }

        public static RimMindError Warn(string message, Exception? inner = null)
        {
            var error = new RimMindError(RimMindErrorCode.InternalError, message)
            {
                InnerException = inner,
                TraceId = TraceContext.Current,
            };
            System.Diagnostics.Debug.WriteLine($"[WARN] {error}");
            OnWarn?.Invoke(message, inner);
            return error;
        }

        public static RimMindError Error(string message, Exception? inner = null)
        {
            var error = new RimMindError(RimMindErrorCode.InternalError, message)
            {
                InnerException = inner,
                TraceId = TraceContext.Current,
            };
            System.Diagnostics.Debug.WriteLine($"[ERROR] {error}");
            OnError?.Invoke(message, inner);
            return error;
        }
    }
}
