using RimMind.Application.Common.Models.Tools;
using RimMind.Domain.Agent.Policies;
using RimMind.Domain.Enums;

namespace RimMind.Application.Features.Tools
{
    /// <summary>
    /// Canonical single-source mapping from <see cref="AutonomyDomain"/> to <see cref="RiskLevel"/>.
    /// Shared across Core, Actions, Advisor, and custom tool authors.
    /// </summary>
    public static class AutonomyDomainRiskMapper
    {
        public static RiskLevel MapToRisk(AutonomyDomain? domain, ToolKind kind = ToolKind.Mutation)
        {
            if (!domain.HasValue)
            {
                return kind == ToolKind.Query ? RiskLevel.Low : RiskLevel.Medium;
            }

            return domain.Value switch
            {
                AutonomyDomain.EmergencySurvival => RiskLevel.Low,
                AutonomyDomain.ResourceMarking => RiskLevel.Low,
                AutonomyDomain.WorktableBills => RiskLevel.Medium,
                AutonomyDomain.BlueprintPlacement => RiskLevel.Medium,
                AutonomyDomain.WorkPriorityModification => RiskLevel.Medium,
                AutonomyDomain.HighRiskOperations => RiskLevel.High,
                _ => RiskLevel.Medium
            };
        }
    }
}
