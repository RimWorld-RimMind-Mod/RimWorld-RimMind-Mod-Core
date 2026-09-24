using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Domain.Agent.Social;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Common.Interfaces.Agent.Social;

public interface IDreamGenerator
{
    bool ShouldDream(IAgentInfo agent);
    Task<Result<DreamEntry, RimMindError>> GenerateDreamAsync(IAgentInfo agent, CancellationToken ct = default);
}
