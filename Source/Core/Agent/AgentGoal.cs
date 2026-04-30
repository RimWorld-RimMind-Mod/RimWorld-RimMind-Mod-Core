using System;
using Verse;

namespace RimMind.Core.Agent
{
    public enum GoalCategory { Survival, Social, Work, Self, Colony }
    public enum GoalStatus { Proposed, Active, Achieved, Abandoned, Expired }

    public class AgentGoal : IExposable
    {
        public string Description = "";
        public GoalCategory Category;
        public float Priority;
        public GoalStatus Status = GoalStatus.Proposed;
        public int DeadlineTick = -1;
        public float Progress;

        public AgentGoal() { }

        public AgentGoal(string description, GoalCategory category, float priority, GoalStatus status = GoalStatus.Proposed)
        {
            Description = description ?? "";
            Category = category;
            Priority = priority;
            Status = status;
        }

        public bool IsExpired => DeadlineTick > 0 && Find.TickManager?.TicksGame > DeadlineTick;

        public void ExposeData()
        {
#pragma warning disable CS8601
            Scribe_Values.Look(ref Description, "description");
#pragma warning restore CS8601
            Description ??= "";
            Scribe_Values.Look(ref Category, "category");
            Scribe_Values.Look(ref Priority, "priority");
            Scribe_Values.Look(ref Status, "status");
            Scribe_Values.Look(ref DeadlineTick, "deadlineTick", -1);
            Scribe_Values.Look(ref Progress, "progress");
        }
    }
}
