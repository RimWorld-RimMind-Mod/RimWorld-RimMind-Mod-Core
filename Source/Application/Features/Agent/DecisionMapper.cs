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

            string? defName = null;
            if (paramsDict != null)
            {
                if (paramsDict.TryGetValue("def_name", out var dn) && !string.IsNullOrEmpty(dn))
                    defName = dn;
                else if (paramsDict.TryGetValue("work_type", out var wt) && !string.IsNullOrEmpty(wt))
                    defName = wt;
                else if (paramsDict.TryGetValue("job_type", out var jt) && !string.IsNullOrEmpty(jt))
                    defName = jt;

                if (mechanismId == "pawn.work")
                {
                    if (paramsDict.TryGetValue("priority", out var prio) && !string.IsNullOrEmpty(prio))
                        valueJson = prio;
                    else if (paramsDict.TryGetValue("value", out var val) && !string.IsNullOrEmpty(val))
                        valueJson = val;
                    else
                        valueJson = "1";
                }
            }

            return new MechanismWriteArgs
            {
                MechanismId = mechanismId,
                PawnId = pawnId,
                DefName = defName,
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

            if (string.Equals(actionIntent, "prioritize_work", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(actionIntent, "set_work_priority", System.StringComparison.OrdinalIgnoreCase))
                return ("pawn.work", "set_priority");

            if (string.Equals(actionIntent, "take_job", System.StringComparison.OrdinalIgnoreCase))
                return ("pawn.job", "assign_work");

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
