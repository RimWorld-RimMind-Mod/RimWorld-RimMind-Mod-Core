using System.Collections.Generic;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Models.Agent;

namespace RimMind.Application.Common.Interfaces.Agent
{
    public interface IScopedAgentManager
    {
        IScopedAgent GetOrCreate(AgentScope scope, IAgentBus agentBus);
        IScopedAgent GetOrCreate(string scopeType, string scopeId, IAgentBus agentBus, int? mapId = null);
        IScopedAgent? Find(AgentScope scope);
        IScopedAgent? Find(string scopeType, string scopeId);
        IReadOnlyList<IScopedAgent> GetAll();
        bool Remove(AgentScope scope);
        bool Remove(string scopeType, string scopeId);
        void Clear();
    }
}
