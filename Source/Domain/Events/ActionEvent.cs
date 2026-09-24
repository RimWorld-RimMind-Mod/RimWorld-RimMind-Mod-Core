namespace RimMind.Domain.Events
{
    public class ActionEvent : AgentBusEvent
    {
        public string ActionName = "";
        public bool Success;
        public string ResultReason = "";
        public string EventId = "";

        public ActionEvent(string npcId, int pawnId, string actionName, bool success, string resultReason, string eventId, int timestamp = 0)
            : base(npcId, pawnId, AgentBusEventType.Action, timestamp)
        {
            ActionName = actionName;
            Success = success;
            ResultReason = resultReason;
            EventId = eventId;
        }
    }
}
