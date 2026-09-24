using System.Collections.Generic;

namespace RimMind.Application.Common.Models.Mechanisms
{
    public sealed record MechanismWriteArgs
    {
        public string MechanismId { get; init; } = "";
        public int PawnId { get; init; }
        public int? MapId { get; init; }
        public string? DefName { get; init; }
        public string Action { get; init; } = "";
        public string? ValueJson { get; init; }
        public string? TraceId { get; init; }
        public Dictionary<string, string>? Params { get; init; }
    }
}
