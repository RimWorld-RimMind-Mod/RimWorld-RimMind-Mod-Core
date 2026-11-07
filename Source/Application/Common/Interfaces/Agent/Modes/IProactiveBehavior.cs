using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces.Extension;

namespace RimMind.Application.Common.Interfaces.Agent.Modes
{
    public interface IProactiveBehavior : IExtension
    {
        string BehaviorId { get; }
        int Order { get; }
        bool ShouldExecute(IAgentInfo agent, int currentTick);
        Task ExecuteAsync(IAgentInfo agent, CancellationToken ct);
    }
}
