namespace RimMind.Domain.Events;

public class MentalStateWarningEvent : AgentBusEvent
{
    public float BreakThreshold;
    public float CurrentMoodLevel;
    public string WarningLevel = "";

    public MentalStateWarningEvent() : base() { }

    public MentalStateWarningEvent(string npcId, int pawnId,
        float breakThreshold, float currentMoodLevel, string warningLevel, int timestamp = 0)
        : base(npcId, pawnId, AgentBusEventType.MentalStateWarning, timestamp)
    {
        BreakThreshold = breakThreshold;
        CurrentMoodLevel = currentMoodLevel;
        WarningLevel = warningLevel;
    }
}
