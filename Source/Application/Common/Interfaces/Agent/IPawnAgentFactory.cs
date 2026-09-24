using RimMind.Application.Common.Interfaces;

namespace RimMind.Application.Common.Interfaces.Agent
{
    public interface IPawnAgentFactory
    {
        void SerializeAgent(ref IPawnAgent? agent, string label);
    }
}
