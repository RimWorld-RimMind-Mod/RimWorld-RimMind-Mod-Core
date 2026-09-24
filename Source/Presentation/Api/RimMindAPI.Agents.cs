using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Domain.Enums;
using RimMind.Presentation.Runtime;
using RimMind.Presentation.Runtime.Services;
using Verse;

namespace RimMind.Presentation.Api
{
    public static partial class RimMindAPI
    {
        public static class Agents
        {
            private static readonly RuntimeServiceRef<IScopedAgentManager> Managers =
                RuntimeServiceRef<IScopedAgentManager>.Optional();

            public static IScopedAgent? FindScoped(string scopeType, string scopeId)
            {
                var manager = Managers.ValueOrDefault;
                return manager?.Find(scopeType, scopeId);
            }

            public static IScopedAgent? GetOrCreateScoped(string scopeType, string scopeId, int? mapId = null)
            {
                var scope = RuntimeServiceHub.Shared.Capture();
                var manager = scope.GetOptional<IScopedAgentManager>();
                var bus = scope.GetOptional<IAgentBus>();
                if (manager == null || bus == null)
                {
                    Log.Warning("[RimMind-Core] Scoped agent services are not available.");
                    return null;
                }

                return manager.GetOrCreate(scopeType, scopeId, bus, mapId);
            }

            public static bool StartScoped(string scopeType, string scopeId, int? mapId = null)
            {
                var agent = GetOrCreateScoped(scopeType, scopeId, mapId);
                return agent != null && agent.TransitionTo(AgentState.Active);
            }

            public static bool PauseScoped(string scopeType, string scopeId)
            {
                var agent = FindScoped(scopeType, scopeId);
                return agent != null && agent.TransitionTo(AgentState.Paused);
            }

            public static bool ForceThinkScoped(string scopeType, string scopeId)
            {
                var agent = FindScoped(scopeType, scopeId);
                if (agent == null) return false;

                agent.ForceThink();
                return true;
            }
        }
    }
}
