namespace RimMind.Domain.Events;

public class InformationDiffusionEvent : AgentBusEvent
{
    public string RumorId = "";
    public string Content = "";
    public string SourceNpcId = "";
    public float Importance;
    public int DistortionLevel;

    public InformationDiffusionEvent() : base() { }

    public InformationDiffusionEvent(string npcId, int pawnId, string rumorId, string content,
        string sourceNpcId, float importance, int distortionLevel, int timestamp = 0)
        : base(npcId, pawnId, AgentBusEventType.InformationDiffusion, timestamp)
    {
        RumorId = rumorId;
        Content = content;
        SourceNpcId = sourceNpcId;
        Importance = importance;
        DistortionLevel = distortionLevel;
    }
}
