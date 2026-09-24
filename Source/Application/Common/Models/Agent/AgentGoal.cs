namespace RimMind.Application.Common.Models.Agent
{
    public enum GoalStatus
    {
        Proposed,
        Active,
        Achieved,
        Expired,
        Abandoned
    }

    public enum GoalCategory
    {
        Survival,
        Work,
        Social,
        Other
    }

    public class AgentGoal
    {
        public string GoalId = "";
        public string Description = "";
        public float Priority;
        public GoalStatus Status = GoalStatus.Proposed;
        public GoalCategory Category = GoalCategory.Other;
        public float Progress;
        public int ExpirationTick;

        public bool IsCompleted => Status == GoalStatus.Achieved;

        public bool IsExpired(int currentTick) => ExpirationTick > 0 && currentTick > ExpirationTick;

        public AgentGoal() { }

        public AgentGoal(string description, GoalCategory category, float priority, GoalStatus status)
        {
            GoalId = System.Guid.NewGuid().ToString("N").Substring(0, 8);
            Description = description;
            Category = category;
            Priority = priority;
            Status = status;
        }

        public override string ToString() => $"AgentGoal({Description}, P={Priority:F1}, {Status})";
    }
}
