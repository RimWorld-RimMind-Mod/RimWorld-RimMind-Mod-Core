using RimMind.Domain.Agent.Modes;
using RimMind.Domain.Enums;

namespace RimMind.Application.Common.Interfaces.Agent
{
    /// <summary>
    /// Lifecycle control methods for an agent — ticking, state transitions, mode switching, and cleanup.
    /// </summary>
    public interface IAgentLifecycle
    {
        void Tick();
        bool TransitionTo(AgentState newState);
        void ForceThink();
        void SwitchMode(AgentModeId modeId);
        void Cleanup();
        void Destroy();
        void ResubscribeEvents();
    }
}
