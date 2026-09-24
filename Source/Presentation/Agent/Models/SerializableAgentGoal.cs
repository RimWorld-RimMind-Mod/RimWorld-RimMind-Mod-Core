using Verse;

namespace RimMind.Application.Common.Models.Agent
{
    /// <summary>
    /// Verse-serializable AgentGoal.
    /// Subclass in Presentation layer so Application layer stays Verse-free.
    /// PawnAgent serialization uses this type with Scribe_Deep / Scribe_Collections.
    /// </summary>
    public class SerializableAgentGoal : AgentGoal, IExposable
    {
        public SerializableAgentGoal() { }

        public SerializableAgentGoal(string description, GoalCategory category, float priority, GoalStatus status)
            : base(description, category, priority, status) { }

        public void ExposeData()
        {
            Scribe_Values.Look(ref GoalId, "goalId", "");
            Scribe_Values.Look(ref Description, "description", "");
            Scribe_Values.Look(ref Priority, "priority", 0f);
            Scribe_Values.Look(ref Status, "status", GoalStatus.Proposed);
            Scribe_Values.Look(ref Category, "category", GoalCategory.Other);
            Scribe_Values.Look(ref Progress, "progress", 0f);
            Scribe_Values.Look(ref ExpirationTick, "expirationTick", 0);
        }
    }
}
