using System;
using System.Collections.Generic;
using RimMind.Domain.Enums;

namespace RimMind.Domain.Agent.Social;

public sealed record SocialEventPlan
{
    public string EventId { get; init; } = "";
    public SocialEventType EventType { get; init; }
    public string OrganizerNpcId { get; init; } = "";
    public string Description { get; init; } = "";
    public int ScheduledTick { get; init; }
    public int DurationTicks { get; init; }
    public IReadOnlyList<string> InvitedNpcIds { get; init; } = Array.Empty<string>();
    public string? LocationHint { get; init; }
}
