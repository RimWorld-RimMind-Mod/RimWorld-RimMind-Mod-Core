using System;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using RimMind.Application.Common.Constants;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Domain.Agent.Modes;

namespace RimMind.Infrastructure.Mechanisms.Pawn.Job
{
    /// <summary>
    /// Adapts LLM tool arguments for Core's <c>pawn.job.set</c> / <c>pawn_job_set</c> tool:
    /// prevents plain-text scalar parameters from being misread as <c>def_name</c> work targets,
    /// and infers concrete sub-actions (<c>assign_work</c>, <c>move_to</c>, <c>eat_food</c>) without
    /// silently defaulting argument-less calls to <c>force_rest</c>.
    /// </summary>
    public sealed class JobMechanismArgumentAdapter : IToolArgumentAdapter
    {
        private static readonly Regex FoodKeywordPattern = new Regex(
            @"\b(eat|eating|food|meal|meals|hunger|hungry|starving|starvation|nutrition)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public string Id => "core.tool_adapter.pawn_job";

        public string OwnerModId => RimMindOwnerConsts.CoreModId;

        public bool CanAdapt(string toolId)
        {
            return string.Equals(toolId, "pawn.job.set", StringComparison.OrdinalIgnoreCase)
                || string.Equals(toolId, "pawn_job_set", StringComparison.OrdinalIgnoreCase);
        }

        public JObject AdaptArguments(string toolId, JObject args, AgentDecision decision, int callerPawnId)
        {
            if (args == null)
                args = new JObject();

            // Plain-text Param is placed into def_name by generic scalar fallback;
            // for pawn.job.set a scalar phrase like "eat a meal" must not force assign_work.
            string? rawScalar = decision?.Param?.Trim().Trim('"');
            if (!string.IsNullOrWhiteSpace(rawScalar)
                && !rawScalar!.StartsWith("{", StringComparison.Ordinal)
                && args["def_name"] != null
                && string.Equals(args.Value<string>("def_name"), rawScalar, StringComparison.Ordinal))
            {
                args.Remove("def_name");
            }

            string? currentAction = args.Value<string>("action");
            if (string.IsNullOrWhiteSpace(currentAction)
                || string.Equals(currentAction, "set", StringComparison.OrdinalIgnoreCase))
            {
                if (args["work_type"] != null || args["job_type"] != null || args["def_name"] != null)
                {
                    args["action"] = "assign_work";
                }
                else if (args["cell_x"] != null || args["x"] != null)
                {
                    args["action"] = "move_to";
                }
                else if (ContainsFoodKeyword(decision?.Param) || ContainsFoodKeyword(decision?.Reason))
                {
                    args["action"] = "eat_food";
                }
                // Otherwise leave 'action' unset so JobMechanism rejects with the valid operations list.
            }

            return args;
        }

        private static bool ContainsFoodKeyword(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return false;
            return FoodKeywordPattern.IsMatch(text!);
        }
    }
}
