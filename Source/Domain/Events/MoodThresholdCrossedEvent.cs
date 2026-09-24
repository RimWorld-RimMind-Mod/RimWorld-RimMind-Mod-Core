using RimMind.Domain.Enums;

namespace RimMind.Domain.Events;

public class MoodThresholdCrossedEvent : AgentBusEvent
{
    public float PreviousMoodLevel;
    public float CurrentMoodLevel;
    public MoodThreshold Threshold;

    public MoodThresholdCrossedEvent() : base() { }

    public MoodThresholdCrossedEvent(string npcId, int pawnId,
        float previousLevel, float currentLevel, MoodThreshold threshold, int timestamp = 0)
        : base(npcId, pawnId, AgentBusEventType.MoodThreshold, timestamp)
    {
        PreviousMoodLevel = previousLevel;
        CurrentMoodLevel = currentLevel;
        Threshold = threshold;
    }
}
