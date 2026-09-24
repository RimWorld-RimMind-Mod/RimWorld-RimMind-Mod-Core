using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Models.Agent;

namespace RimMind.Application.Common.Interfaces.Agent
{
    public interface IScopedAgentFactory
    {
        IScopedAgent Create(AgentScope scope, IAgentBus agentBus);
        IScopedAgent Create(string scopeType, string scopeId, IAgentBus agentBus, int? mapId = null);
    }
}
