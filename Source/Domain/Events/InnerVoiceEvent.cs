namespace RimMind.Domain.Events;

public class InnerVoiceEvent : AgentBusEvent
{
    public string VoiceText = "";
    public int ExpiryTick;

    public InnerVoiceEvent() : base() { }

    public InnerVoiceEvent(string npcId, int pawnId, string voiceText, int expiryTick, int timestamp = 0)
        : base(npcId, pawnId, AgentBusEventType.InnerVoice, timestamp)
    {
        VoiceText = voiceText;
        ExpiryTick = expiryTick;
    }
}
