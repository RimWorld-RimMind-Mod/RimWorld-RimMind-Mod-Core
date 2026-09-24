using RimMind.Application.Common.Models.Agent;

namespace RimMind.Application.Common.Interfaces.Agent
{
    public interface IAgentLoopScheduler
    {
        long Generation { get; }
        bool Register(string key, AgentLoopKind kind, IAgentControl agent);
        bool Unregister(string key);
        IAgentControl? Find(string key);
        void Tick(int currentTick);
        void Clear();
        AgentLoopSnapshot GetSnapshot();
    }
}
