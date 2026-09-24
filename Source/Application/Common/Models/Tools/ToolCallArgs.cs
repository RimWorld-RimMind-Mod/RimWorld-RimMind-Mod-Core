using System.Threading;

namespace RimMind.Application.Common.Models.Tools
{
    public sealed record ToolCallArgs
    {
        public string ToolCallId { get; init; } = "";
        public string ToolName { get; init; } = "";
        public string ArgumentsJson { get; init; } = "{}";
        public int? PawnId { get; init; }
        public string? NpcId { get; init; }
        public CancellationToken Ct { get; init; }
        public string? TraceId { get; init; }
    }
}
