using RimMind.Application.Common.Interfaces.Agent.Modes;
using RimMind.Domain.Agent.Modes;

namespace RimMind.Application.Common.Interfaces.Agent
{
    /// <summary>
    /// Read-only state query methods for an agent. No side effects.
    /// </summary>
    public interface IAgentState
    {
        bool IsActive { get; }
        AgentModeId CurrentModeId { get; }
        IAgentMode CurrentMode { get; }
        bool IsPawnValid { get; }
        string GetDebugInfo();
    }
}
