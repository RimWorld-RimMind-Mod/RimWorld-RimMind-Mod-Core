using RimMind.Application.Common.Interfaces.Agent;

namespace RimMind.Application.Common.Interfaces.Agent.Social;

public interface ISleepDetector
{
    bool IsSleeping(IAgentInfo agent);
}
