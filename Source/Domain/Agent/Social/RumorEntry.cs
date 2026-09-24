namespace RimMind.Domain.Agent.Social;

public sealed record RumorEntry
{
    public string RumorId { get; init; } = "";
    public string Content { get; init; } = "";
    public string SourceNpcId { get; init; } = "";
    public float Importance { get; init; }
    public int CreatedTick { get; init; }
    public int DistortionLevel { get; init; }
    public string? OriginalContent { get; init; }
}
