using RimMind.Domain.Enums;

namespace RimMind.Domain.Events;

public class SocialEventProposedEvent : AgentBusEvent
{
    public string EventId = "";
    public SocialEventType EventType;
    public string OrganizerNpcId = "";
    public string Description = "";
    public int ScheduledTick;

    public SocialEventProposedEvent() : base() { }

    public SocialEventProposedEvent(string npcId, int pawnId, string eventId,
        SocialEventType eventType, string organizerNpcId, string description, int scheduledTick, int timestamp = 0)
        : base(npcId, pawnId, AgentBusEventType.SocialEventProposed, timestamp)
    {
        EventId = eventId;
        EventType = eventType;
        OrganizerNpcId = organizerNpcId;
        Description = description;
        ScheduledTick = scheduledTick;
    }
}
