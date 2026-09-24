namespace RimMind.Domain.Events
{
    public class AgentLifecycleEvent : AgentBusEvent
    {
        public string PreviousState = "";
        public string NewState = "";

        public AgentLifecycleEvent(string npcId, int pawnId, string previousState, string newState, int timestamp = 0)
            : base(npcId, pawnId, AgentBusEventType.Lifecycle, timestamp)
        {
            PreviousState = previousState;
            NewState = newState;
        }
    }
}
