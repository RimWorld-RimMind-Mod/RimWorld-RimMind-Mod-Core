namespace RimMind.Domain.Events
{
    public class PerceptionEvent : AgentBusEvent
    {
        public string PerceptionType = "";
        public string Content = "";
        public float Importance;

        public PerceptionEvent(string npcId, int pawnId, string perceptionType, string content, float importance = 0f, int timestamp = 0)
            : base(npcId, pawnId, AgentBusEventType.Perception, timestamp)
        {
            PerceptionType = perceptionType;
            Content = content;
            Importance = importance;
        }
    }
}
