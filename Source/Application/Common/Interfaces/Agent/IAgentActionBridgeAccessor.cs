using RimMind.Application.Common.Interfaces.Extension;

namespace RimMind.Application.Common.Interfaces.Agent
{
    public interface IAgentActionBridgeAccessor
    {
        IAgentActionBridge Current { get; }
    }
}
