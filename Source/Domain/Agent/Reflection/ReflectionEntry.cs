namespace RimMind.Domain.Agent.Reflection;

public sealed record ReflectionEntry
{
    public string Question { get; init; } = "";
    public string Insight { get; init; } = "";
    public int Timestamp { get; init; }
    public string SourceMemoryIds { get; init; } = "";
}
