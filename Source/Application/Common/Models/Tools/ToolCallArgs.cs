using System.Threading;

namespace RimMind.Application.Common.Models.Tools
{
    public sealed record ToolCallArgs
    {
        public string ToolCallId { get; init; } = "";
        public string ToolName { get; init; } = "";
        public string ArgumentsJson { get; init; } = "{}";
        public int? PawnId { get; init; }
        public int? ContextPawnId { get => PawnId; init => PawnId = value; }
        public string? NpcId { get; init; }
        public CancellationToken Ct { get; init; }
        public string? TraceId { get; init; }

        /// <summary>
        /// True only when trusted host code (e.g. an approval card the player clicked) has explicitly
        /// approved this call. Must never be derived from LLM-supplied <see cref="ArgumentsJson"/>;
        /// handlers that honor approval must read this flag instead of any JSON field.
        /// </summary>
        public bool IsPlayerApproved { get; init; }
    }
}
