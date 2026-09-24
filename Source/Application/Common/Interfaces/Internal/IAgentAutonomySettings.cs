using RimMind.Domain.Enums;

namespace RimMind.Application.Common.Interfaces.Internal
{
    /// <summary>
    /// Settings controlling agent autonomy level and risk-based action approval.
    /// </summary>
    public interface IAgentAutonomySettings
    {
        /// <summary>
        /// Current autonomy level for agent decisions.
        /// </summary>
        AgentAutonomyLevel AutonomyLevel { get; set; }

        /// <summary>
        /// Determines whether an action at the given risk level should be auto-approved
        /// based on the current autonomy level.
        /// </summary>
        bool ShouldApproveAction(RiskLevel risk);
    }
}
