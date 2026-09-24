using System.Collections.Generic;
using RimMind.Application.Common.Interfaces.Extension;
using RimMind.Application.Common.Models.Mechanisms;
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
}
