namespace RimMind.Core.AgentBus
{
    public class PerceptionEvent : AgentBusEvent
    {
        public string PerceptionType { get; }
        public string Content { get; }
        public float Importance { get; }
        public int PawnId { get; }

        public PerceptionEvent(string sourceNpcId, int pawnId, string perceptionType, string content, float importance = 0.5f)
            : base(AgentBusEventType.Perception, sourceNpcId)
        {
            PawnId = pawnId;
            PerceptionType = perceptionType ?? "";
            Content = content ?? "";
            Importance = importance;
        }
    }
}
