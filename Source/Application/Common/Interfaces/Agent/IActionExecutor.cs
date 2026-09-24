using RimMind.Domain.Agent.Modes;
using RimMind.Domain.Common;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Common.Interfaces.Agent
{
    /// <summary>
    /// Maps AgentDecision to concrete game actions via Mechanisms.
    /// </summary>
    public interface IActionExecutor
    {
        /// <summary>
        /// Execute an AgentDecision by finding and invoking the corresponding Mechanism.
        /// </summary>
        Result<Unit, RimMindError> ExecuteDecision(AgentDecision decision, int pawnId);

        /// <summary>
        /// Check if an action intent can be mapped to a registered Mechanism.
        /// </summary>
        bool CanExecute(string actionIntent);
    }
}
