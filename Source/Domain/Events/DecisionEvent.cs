namespace RimMind.Domain.Events
{
    public class DecisionEvent : AgentBusEvent
    {
        public string DecisionType = "";
        public string Reason = "";
        public string Action = "";

        public DecisionEvent(string npcId, int pawnId, string decisionType, string reason, string action, int timestamp = 0)
            : base(npcId, pawnId, AgentBusEventType.Decision, timestamp)
        {
            DecisionType = decisionType;
            Reason = reason;
            Action = action;
        }
    }
}
