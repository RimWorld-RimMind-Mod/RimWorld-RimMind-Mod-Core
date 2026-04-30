using System.Collections.Generic;

namespace RimMind.Core.Agent
{
    public static class AgentStateTransition
    {
        private static readonly Dictionary<AgentState, HashSet<AgentState>> _allowed = new Dictionary<AgentState, HashSet<AgentState>>
        {
            { AgentState.Dormant,    new HashSet<AgentState> { AgentState.Active, AgentState.Terminated } },
            { AgentState.Active,     new HashSet<AgentState> { AgentState.Paused, AgentState.Dormant, AgentState.Terminated } },
            { AgentState.Paused,     new HashSet<AgentState> { AgentState.Active, AgentState.Dormant, AgentState.Terminated } },
            { AgentState.Terminated, new HashSet<AgentState>() },
        };

        public static bool CanTransition(AgentState from, AgentState to)
        {
            return _allowed.TryGetValue(from, out var targets) && targets.Contains(to);
        }
    }
}
