using System.Collections.Generic;
using RimMind.Domain.Enums;

namespace RimMind.Application.Common.Models.Agent
{
    public static class AgentStateTransition
    {
        private static readonly Dictionary<AgentState, HashSet<AgentState>> AllowedTransitions =
            new Dictionary<AgentState, HashSet<AgentState>>
            {
                { AgentState.Dormant, new HashSet<AgentState> { AgentState.Active, AgentState.Terminated } },
                { AgentState.Active, new HashSet<AgentState> { AgentState.Paused, AgentState.Dormant, AgentState.Terminated } },
                { AgentState.Paused, new HashSet<AgentState> { AgentState.Active, AgentState.Dormant, AgentState.Terminated } },
                { AgentState.Terminated, new HashSet<AgentState>() },
            };

        public static bool CanTransition(AgentState from, AgentState to)
        {
            if (from == to) return false;
            return AllowedTransitions.TryGetValue(from, out var targets) && targets.Contains(to);
        }
    }
}
