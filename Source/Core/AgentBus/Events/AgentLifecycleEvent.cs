namespace RimMind.Core.AgentBus
{
    public class AgentLifecycleEvent : AgentBusEvent
    {
        public string PreviousState { get; }
        public string NewState { get; }
        public int PawnId { get; }

        public AgentLifecycleEvent(string sourceNpcId, int pawnId, string previousState, string newState)
            : base(AgentBusEventType.AgentLifecycle, sourceNpcId)
        {
            PawnId = pawnId;
            PreviousState = previousState ?? "";
            NewState = newState ?? "";
        }
    }
}
