namespace RimMind.Domain.Events
{
    public sealed class AgentActionOutcomeEvent : AgentBusEvent
    {
        public string ActionIntent { get; init; } = "";
        public bool Succeeded { get; init; }
        public string Summary { get; init; } = "";

        public AgentActionOutcomeEvent() : base() { }

        public AgentActionOutcomeEvent(
            string npcId,
            int pawnId,
            string actionIntent,
            bool succeeded,
            string summary,
            int timestamp = 0)
            : base(npcId, pawnId, AgentBusEventType.Action, timestamp)
        {
            ActionIntent = actionIntent ?? "";
            Succeeded = succeeded;
            Summary = summary ?? "";
        }
    }
}
