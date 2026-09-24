using System;
using System.Collections.Generic;
using RimMind.Application.Common.Models.Agent;
using RimMind.Domain.Enums;

namespace RimMind.Application.Common.Models.Tools
{
    public sealed record ToolManifest
    {
        public string OwnerModId { get; init; } = "RimMind.Core";
        public IReadOnlyList<AgentScopeKind> AllowedScopes { get; init; } =
            Array.AsReadOnly((AgentScopeKind[])Enum.GetValues(typeof(AgentScopeKind)));
        public RiskLevel RiskLevel { get; init; } = RiskLevel.Medium;
        public bool RequiresApproval { get; init; }
        public string SchemaVersion { get; init; } = "1";

        public static ToolManifest Default { get; } = new ToolManifest();
    }
}
