namespace RimMind.Domain.Agent.Psychology;

public sealed record RimMindDynamicThought
{
    public string ThoughtText { get; init; } = "";
    public float MoodOffset { get; init; }
    public int CreatedTick { get; init; }
    public int DurationTicks { get; init; }
    public string Source { get; init; } = "";
}
