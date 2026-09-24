using RimMind.Application.Common.Constants;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Interfaces.Agent.Modes;
using RimMind.Application.Common.Interfaces.Extension;
using RimMind.Domain.Agent.Modes;

namespace RimMind.Application.Features.Agent.Modes
{
    /// <summary>
    /// Default mode transition policy that allows all transitions.
    /// This ensures zero breaking change when no custom policies are registered.
    /// </summary>
    public class DefaultModeTransitionPolicy : IModeTransitionPolicy
    {
        public string Id => "DefaultModeTransitionPolicy";
        public string OwnerModId => RimMindOwnerConsts.CoreModId;
        public string? DenyReason => null;

        public bool CanTransition(IAgentInfo agent, AgentModeId from, AgentModeId to)
        {
            return true;
        }
    }
}
