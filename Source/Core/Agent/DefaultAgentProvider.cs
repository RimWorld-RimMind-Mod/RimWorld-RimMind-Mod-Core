using System.Collections.Generic;
using RimMind.Core.Comps;
using Verse;

namespace RimMind.Core.Agent
{
    public class DefaultAgentProvider : IAgentProvider
    {
        public AgentState GetAgentState(Pawn pawn)
        {
            var comp = CompPawnAgent.GetComp(pawn);
            return comp?.Agent?.State ?? AgentState.Dormant;
        }

        public IReadOnlyList<AgentGoal> GetActiveGoals(Pawn pawn)
        {
            var comp = CompPawnAgent.GetComp(pawn);
            return comp?.Agent?.GoalStack.ActiveGoals ?? new List<AgentGoal>();
        }

        public string GetWorkingMemorySummary(Pawn pawn)
        {
            try
            {
                var data = RimMindAPI.GetProviderData("memory_pawn", pawn);
                return data ?? "";
            }
            catch
            {
                return "";
            }
        }

        public bool SetAgentState(Pawn pawn, AgentState state)
        {
            var comp = CompPawnAgent.GetComp(pawn);
            return comp?.Agent?.TransitionTo(state) ?? false;
        }

        public void AddGoal(Pawn pawn, AgentGoal goal)
        {
            var comp = CompPawnAgent.GetComp(pawn);
            comp?.Agent?.AddGoal(goal);
        }

        public bool RemoveGoal(Pawn pawn, string goalDescription)
        {
            var comp = CompPawnAgent.GetComp(pawn);
            return comp?.Agent?.RemoveGoal(goalDescription) ?? false;
        }
    }
}
