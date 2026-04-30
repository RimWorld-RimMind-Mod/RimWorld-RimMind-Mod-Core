namespace RimMind.Core.AgentBus
{
    public class GoalEvent : AgentBusEvent
    {
        public string GoalDescription { get; }
        public string GoalStatus { get; }
        public string GoalCategory { get; }
        public int PawnId { get; }

        public GoalEvent(string sourceNpcId, int pawnId, string goalDescription, string goalStatus, string goalCategory = "")
            : base(AgentBusEventType.Goal, sourceNpcId)
        {
            PawnId = pawnId;
            GoalDescription = goalDescription ?? "";
            GoalStatus = goalStatus ?? "";
            GoalCategory = goalCategory ?? "";
        }
    }
}
