using RimMind.Domain.Enums;

namespace RimMind.Domain.Events;

public class DreamEvent : AgentBusEvent
{
    public string DreamContent = "";
    public DreamType DreamType;
    public float MoodImpact;

    public DreamEvent() : base() { }

    public DreamEvent(string npcId, int pawnId, string dreamContent,
        DreamType dreamType, float moodImpact, int timestamp = 0)
        : base(npcId, pawnId, AgentBusEventType.Dream, timestamp)
    {
        DreamContent = dreamContent;
        DreamType = dreamType;
        MoodImpact = moodImpact;
    }
}
