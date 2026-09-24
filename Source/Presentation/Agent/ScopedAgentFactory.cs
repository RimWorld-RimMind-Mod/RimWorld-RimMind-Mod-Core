using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Models.Agent;

namespace RimMind.Presentation.Agent
{
    public sealed class ScopedAgentFactory : IScopedAgentFactory
    {
        public IScopedAgent Create(AgentScope scope, IAgentBus agentBus)
        {
            return new ScopedAgent(scope, agentBus);
        }

        public IScopedAgent Create(string scopeType, string scopeId, IAgentBus agentBus, int? mapId = null)
        {
            return Create(AgentScope.Custom(scopeType, scopeId, mapId), agentBus);
        }
    }
}
