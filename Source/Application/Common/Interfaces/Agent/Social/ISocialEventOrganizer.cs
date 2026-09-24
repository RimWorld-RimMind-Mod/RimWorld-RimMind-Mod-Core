using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Domain.Agent.Social;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Common.Interfaces.Agent.Social;

public interface ISocialEventOrganizer
{
    bool ShouldOrganize(IAgentInfo agent);
    Task<Result<SocialEventPlan, RimMindError>> OrganizeAsync(IAgentInfo agent, CancellationToken ct = default);
    IReadOnlyList<SocialEventPlan> GetPendingEvents();
    void MarkEventExecuted(string eventId);
}
