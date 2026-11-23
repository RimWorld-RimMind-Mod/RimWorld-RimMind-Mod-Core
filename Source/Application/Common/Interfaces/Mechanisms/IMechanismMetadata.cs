using System.Collections.Generic;
using RimMind.Application.Common.Interfaces.Extension;
using RimMind.Application.Common.Models.Mechanisms;
using RimMind.Application.Common.Models.Tools;
using RimMind.Domain.Agent.Policies;
using RimMind.Domain.Enums;

namespace RimMind.Application.Common.Interfaces.Mechanisms
{
    public interface IMechanismMetadata : IExtension
    {
        string MechanismId { get; }
        MechanismScope Scope { get; }
        MechanismRisk Risk { get; }
        IReadOnlyList<MechanismOperationType> SupportedOperations { get; }
        MechanismDocs Docs { get; }
        IReadOnlyList<MechanismActionInfo>? GetWriteActions();
        MechanismRisk GetRiskForOperation(MechanismOperationType operation);
    }

    /// <summary>
    /// Optional additive interface allowing mechanisms to declaratively announce the
    /// <see cref="AutonomyDomain"/> governed by each operation instead of hardcoding mechanism IDs
    /// in <c>MechanismToolHandler</c>.
    /// </summary>
    public interface IMechanismAutonomyMetadata : IMechanismMetadata
    {
        AutonomyDomain? GetAutonomyDomainForOperation(MechanismOperationType operation);
    }

    /// <summary>
    /// Optional additive interface allowing mechanisms to declaratively announce the
    /// <see cref="ToolScope"/> for each operation instead of relying on name heuristics
    /// in <c>MechanismToolHandler</c> or <c>ToolScopeProjection</c>.
    /// </summary>
    public interface IMechanismScopeMetadata : IMechanismMetadata
    {
        ToolScope? GetToolScopeForOperation(MechanismOperationType operation);
    }
}
