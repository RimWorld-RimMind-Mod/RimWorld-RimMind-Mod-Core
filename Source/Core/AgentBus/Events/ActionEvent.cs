namespace RimMind.Core.AgentBus
{
    public class ActionEvent : AgentBusEvent
    {
        public string EventId { get; }
        public string ActionName { get; }
        public bool Success { get; }
        public string Reason { get; }
        public string ResultReason { get; }
        public string TargetLabel { get; }
        public int PawnId { get; }

        public ActionEvent(string sourceNpcId, int pawnId, string actionName, bool success, string reason = "", string targetLabel = "", string eventId = "", string resultReason = "")
            : base(AgentBusEventType.Action, sourceNpcId)
        {
            EventId = eventId ?? "";
            PawnId = pawnId;
            ActionName = actionName ?? "";
            Success = success;
            Reason = reason ?? "";
            ResultReason = resultReason ?? "";
            TargetLabel = targetLabel ?? "";
        }
    }
}
