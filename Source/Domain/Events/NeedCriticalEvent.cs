using RimMind.Domain.Enums;

namespace RimMind.Domain.Events;

public class NeedCriticalEvent : AgentBusEvent
{
    public string NeedId = "";
    public float CurrentLevel;
    public NeedUrgency Urgency;

    public NeedCriticalEvent() : base() { }

    public NeedCriticalEvent(string npcId, int pawnId,
        string needId, float currentLevel, NeedUrgency urgency, int timestamp = 0)
        : base(npcId, pawnId, AgentBusEventType.NeedCritical, timestamp)
    {
        NeedId = needId;
        CurrentLevel = currentLevel;
        Urgency = urgency;
    }
}
