using RimMind.Domain.Enums;

namespace RimMind.Domain.Events;

public class TraitEvolutionEvent : AgentBusEvent
{
    public string TraitDefName = "";
    public TraitEvolutionKind Kind;
    public string Reason = "";
    public float Confidence;

    public TraitEvolutionEvent() : base() { }

    public TraitEvolutionEvent(string npcId, int pawnId, string traitDefName,
        TraitEvolutionKind kind, string reason, float confidence, int timestamp = 0)
        : base(npcId, pawnId, AgentBusEventType.TraitEvolution, timestamp)
    {
        TraitDefName = traitDefName;
        Kind = kind;
        Reason = reason;
        Confidence = confidence;
    }
}
