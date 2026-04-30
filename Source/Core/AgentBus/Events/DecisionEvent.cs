namespace RimMind.Core.AgentBus
{
    public class DecisionEvent : AgentBusEvent
    {
        public string DecisionType { get; }
        public string Reasoning { get; }
        public string ChosenAction { get; }
        public int PawnId { get; }

        public DecisionEvent(string sourceNpcId, int pawnId, string decisionType, string reasoning, string chosenAction)
            : base(AgentBusEventType.Decision, sourceNpcId)
        {
            PawnId = pawnId;
            DecisionType = decisionType ?? "";
            Reasoning = reasoning ?? "";
            ChosenAction = chosenAction ?? "";
        }
    }
}
