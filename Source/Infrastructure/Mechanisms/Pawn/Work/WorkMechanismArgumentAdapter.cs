using System;
using Newtonsoft.Json.Linq;
using RimMind.Application.Common.Constants;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Domain.Agent.Modes;

namespace RimMind.Infrastructure.Mechanisms.Pawn.Work
{
    /// <summary>
    /// Adapts LLM tool arguments for Core's <c>pawn.work.set</c> / <c>pawn_work_set</c> tool:
    /// maps plain-text scalar work type targets (<c>def_name</c>) to <c>work_type</c> with default priority 1.
    /// </summary>
    public sealed class WorkMechanismArgumentAdapter : IToolArgumentAdapter
    {
        public string Id => "core.tool_adapter.pawn_work";

        public string OwnerModId => RimMindOwnerConsts.CoreModId;

        public bool CanAdapt(string toolId)
        {
            return string.Equals(toolId, "pawn.work.set", StringComparison.OrdinalIgnoreCase)
                || string.Equals(toolId, "pawn_work_set", StringComparison.OrdinalIgnoreCase);
        }

        public JObject AdaptArguments(string toolId, JObject args, AgentDecision decision, int callerPawnId)
        {
            if (args == null)
                args = new JObject();

            if (args["work_type"] == null && args["def_name"] != null)
            {
                string? target = args.Value<string>("def_name");
                if (!string.IsNullOrWhiteSpace(target))
                {
                    args["work_type"] = target;
                    if (args["priority"] == null)
                        args["priority"] = 1;
                }
            }

            return args;
        }
    }
}
