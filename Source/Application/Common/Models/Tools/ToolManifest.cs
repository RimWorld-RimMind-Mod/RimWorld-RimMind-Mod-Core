using System;
using System.Collections.Generic;
using RimMind.Application.Common.Models.Agent;
using RimMind.Domain.Agent.Policies;
using RimMind.Domain.Enums;

namespace RimMind.Application.Common.Models.Tools
{
    public enum ToolKind
    {
        Mutation = 0,
        Query = 1
    }

    public sealed record ToolManifest
    {
        public string OwnerModId { get; init; } = "RimMind.Core";
        public IReadOnlyList<AgentScopeKind> AllowedScopes { get; init; } =
            Array.AsReadOnly((AgentScopeKind[])Enum.GetValues(typeof(AgentScopeKind)));
        public ToolKind Kind { get; init; } = ToolKind.Mutation;
        public AutonomyDomain? DefaultDomain { get; init; }
        public string? AutonomyDomainId { get; init; }
        public RiskLevel RiskLevel { get; init; } = RiskLevel.Medium;
        public bool RequiresApproval { get; init; }
        public IReadOnlyList<string> Aliases { get; init; } = Array.Empty<string>();
        public bool IsSelfTargeted { get; init; }
        public string? DisplayName { get; init; }
        public IReadOnlyList<string> SummaryArgKeys { get; init; } = Array.Empty<string>();
        public string SchemaVersion { get; init; } = "1";

        public static ToolManifest Default { get; } = new ToolManifest();
    }
}
