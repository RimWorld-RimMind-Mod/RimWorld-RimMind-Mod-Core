using System.Collections.Generic;
using Verse;

namespace RimMind.Core.Agent
{
    public interface IAgentProvider
    {
        AgentState GetAgentState(Pawn pawn);
        IReadOnlyList<AgentGoal> GetActiveGoals(Pawn pawn);
        string GetWorkingMemorySummary(Pawn pawn);
        bool SetAgentState(Pawn pawn, AgentState state);
        void AddGoal(Pawn pawn, AgentGoal goal);
        bool RemoveGoal(Pawn pawn, string goalDescription);
    }
}
