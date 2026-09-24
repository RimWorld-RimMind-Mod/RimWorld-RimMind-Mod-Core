using RimMind.Application.Common.Interfaces.Extension;
using RimMind.Domain.Agent.Modes;

namespace RimMind.Application.Common.Interfaces.Agent.Modes
{
    /// <summary>
    /// Policy that controls whether a mode transition is allowed.
    /// Register via RimMindAPI.Modes.Policies.Register() to add custom transition rules.
    /// </summary>
    public interface IModeTransitionPolicy : IExtension
    {
        /// <summary>
        /// Check whether the transition from one mode to another is allowed for the given agent.
        /// </summary>
        bool CanTransition(IAgentInfo agent, AgentModeId from, AgentModeId to);

        /// <summary>
        /// If CanTransition returns false, this provides the reason for denial.
        /// </summary>
        string? DenyReason { get; }
    }
}
