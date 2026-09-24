namespace RimMind.Domain.Events
{
    public class DecisionFailedEvent : AgentBusEvent
    {
        public string ActionIntent = "";
        public string Reason = "";

        public DecisionFailedEvent(string npcId, int pawnId, string actionIntent, string reason)
        {
            NpcId = npcId;
            PawnId = pawnId;
            ActionIntent = actionIntent;
            Reason = reason;
            BusEventType = AgentBusEventType.DecisionFailed;
        }
    }
}
