namespace RimMind.Domain.Agent.Psychology;

public sealed record NeedLevel
{
    public string NeedId { get; init; } = "";
    public float CurrentLevel { get; init; }
}
