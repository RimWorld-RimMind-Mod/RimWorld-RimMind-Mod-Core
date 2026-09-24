namespace RimMind.Domain.Events
{
    public class GoalEvent : AgentBusEvent
    {
        public string GoalDescription = "";
        public string Status = "";
        public string Category = "";

        public GoalEvent(string npcId, int pawnId, string goalDescription, string status, string category, int timestamp = 0)
            : base(npcId, pawnId, AgentBusEventType.Goal, timestamp)
        {
            GoalDescription = goalDescription;
            Status = status;
            Category = category;
        }
    }
}
