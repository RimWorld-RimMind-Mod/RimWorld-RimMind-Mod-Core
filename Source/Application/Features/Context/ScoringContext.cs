using System.Collections.Generic;

namespace RimMind.Application.Features.Context
{
    public sealed class ScoringContext
    {
        public string Scenario { get; init; } = string.Empty;
        public int NowTicks { get; init; }
        public string? Query { get; init; }
        public ISet<string> UserPinnedKeys { get; init; } = new HashSet<string>();
    }
}
