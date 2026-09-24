namespace RimMind.Domain.Events;

public class AgentModeChangedEvent : AgentBusEvent
{
    public string OldMode = "";
    public string NewMode = "";

    public AgentModeChangedEvent() : base() { }

    public AgentModeChangedEvent(string npcId, int pawnId, string oldMode, string newMode, int timestamp = 0)
        : base(npcId, pawnId, AgentBusEventType.ModeChange, timestamp)
    {
        OldMode = oldMode;
        NewMode = newMode;
    }
}
