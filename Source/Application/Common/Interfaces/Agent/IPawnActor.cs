using RimMind.Domain.Agent.Modes;
using RimMind.Domain.Common;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Common.Interfaces.Agent
{
    public interface IPawnActor
    {
        void Tick();

        /// <summary>
        /// Execute an AgentDecision by delegating to IActionExecutor.
        /// </summary>
        Result<Unit, RimMindError> ExecuteDecision(AgentDecision decision);
    }
}
