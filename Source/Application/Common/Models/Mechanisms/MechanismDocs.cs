using System.Collections.Generic;

namespace RimMind.Application.Common.Models.Mechanisms
{
    public sealed record MechanismDocs
    {
        public string Summary { get; init; } = "";
        public string? QueryDescription { get; init; }
        public string? SetDescription { get; init; }
        public string? AddDescription { get; init; }
        public string? RemoveDescription { get; init; }
        public string? ToggleDescription { get; init; }
        public string? TriggerDescription { get; init; }
        public string? ListDescription { get; init; }
        public string? WatchDescription { get; init; }
        public IReadOnlyList<string>? Examples { get; init; }
        public IReadOnlyList<string>? Caveats { get; init; }
    }
}
