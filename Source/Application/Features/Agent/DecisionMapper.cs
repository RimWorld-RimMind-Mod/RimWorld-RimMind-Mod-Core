using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RimMind.Application.Common.Models.Mechanisms;
using RimMind.Domain.Agent.Modes;
using RimMind.Domain.Enums;

namespace RimMind.Application.Features.Agent
{
    /// <summary>
    /// Maps AgentDecision fields to MechanismWriteArgs.
    /// Extracts mechanismId, action, and params from the decision's ActionIntent and Param fields.
    /// </summary>
    public static class DecisionMapper
    {
        /// <summary>
        /// Map an AgentDecision to a MechanismWriteArgs for the given pawn.
        /// ActionIntent format: "mechanismId.action" (e.g., "pawn.job.force_rest")
        /// If ActionIntent contains no dot, the entire string is treated as the action
        /// and mechanismId defaults to "pawn.job".
        /// </summary>
        public static MechanismWriteArgs ToWriteArgs(AgentDecision decision, int pawnId)
        {
            var (mechanismId, action) = ParseActionIntent(decision.ActionIntent);
            var (valueJson, paramsDict) = ParseParam(decision.Param);

            if (decision.TargetPawnId != null)
            {
                paramsDict = paramsDict ?? new Dictionary<string, string>();
                paramsDict["target_pawn_id"] = decision.TargetPawnId;
            }

            return new MechanismWriteArgs
            {
                MechanismId = mechanismId,
                PawnId = pawnId,
                Action = action,
                ValueJson = valueJson,
                Params = paramsDict,
                TraceId = decision.ToolCallId,
            };
        }

        /// <summary>
        /// Infer the preferred MechanismOperationType from the action prefix.
        /// </summary>
        public static MechanismOperationType InferOperationType(string action)
        {
            if (string.IsNullOrEmpty(action)) return MechanismOperationType.Set;

            if (action.StartsWith("force_") || action.StartsWith("trigger_") || action.StartsWith("emergency_"))
                return MechanismOperationType.Trigger;
            if (action.StartsWith("set_") || action.StartsWith("adjust_") || action.StartsWith("configure_"))
                return MechanismOperationType.Set;
            if (action.StartsWith("add_") || action.StartsWith("grant_") || action.StartsWith("give_"))
                return MechanismOperationType.Add;
            if (action.StartsWith("toggle_") || action.StartsWith("switch_"))
                return MechanismOperationType.Toggle;
            if (action.StartsWith("remove_") || action.StartsWith("revoke_") || action.StartsWith("clear_"))
                return MechanismOperationType.Remove;

            return MechanismOperationType.Set;
        }

        /// <summary>
        /// Parse ActionIntent into (mechanismId, action).
        /// "pawn.job.force_rest" -> ("pawn.job", "force_rest")
        /// "force_rest" -> ("pawn.job", "force_rest")
        /// </summary>
        public static (string mechanismId, string action) ParseActionIntent(string actionIntent)
        {
            if (string.IsNullOrEmpty(actionIntent))
                return ("pawn.job", "");

            var lastDot = actionIntent.LastIndexOf('.');
            if (lastDot <= 0)
                return ("pawn.job", actionIntent);

            return (actionIntent.Substring(0, lastDot), actionIntent.Substring(lastDot + 1));
        }

        /// <summary>
        /// Parse the Param field into (valueJson, paramsDict).
        /// If Param is a JSON object, extract key-value pairs into paramsDict.
        /// </summary>
        internal static (string? valueJson, Dictionary<string, string>? paramsDict) ParseParam(string? param)
        {
            if (string.IsNullOrEmpty(param))
                return (null, null);

            string? valueJson = null;
            Dictionary<string, string>? paramsDict = null;

            try
            {
                var obj = JToken.Parse(param!);
                if (obj is JObject jObj)
                {
                    paramsDict = new Dictionary<string, string>();
                    foreach (var prop in jObj.Properties())
                    {
                        paramsDict[prop.Name] = prop.Value?.ToString() ?? "";
                    }
                    valueJson = param;
                }
                else
                {
                    valueJson = param;
                }
            }
            catch (JsonReaderException)
            {
                valueJson = param;
            }

            return (valueJson, paramsDict);
        }
    }
}
