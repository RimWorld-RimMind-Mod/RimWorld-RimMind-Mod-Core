using System;
using System.Collections.Generic;
using RimMind.Domain.Enums;

namespace RimMind.Domain.Agent.Social;

public sealed record DreamEntry
{
    public string DreamId { get; init; } = "";
    public string NpcId { get; init; } = "";
    public string DreamContent { get; init; } = "";
    public DreamType DreamType { get; init; }
    public float MoodImpact { get; init; }
    public int Tick { get; init; }
    public IReadOnlyList<string> SourceMemoryIds { get; init; } = Array.Empty<string>();
}
