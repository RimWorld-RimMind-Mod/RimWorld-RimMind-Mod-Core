using RimMind.Domain.Enums;

namespace RimMind.Domain.Agent.Social;

public sealed record TraitEvolutionRecord
{
    public string TraitDefName { get; init; } = "";
    public TraitEvolutionKind Kind { get; init; }
    public string Reason { get; init; } = "";
    public int Tick { get; init; }
    public float Confidence { get; init; }
}
