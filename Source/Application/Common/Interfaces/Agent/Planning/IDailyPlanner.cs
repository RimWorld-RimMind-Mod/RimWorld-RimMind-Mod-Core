using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Domain.Agent.Planning;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Common.Interfaces.Agent.Planning;

public interface IDailyPlanner
{
    bool ShouldPlan(IAgentInfo agent);

    Task<Result<IReadOnlyList<ScheduleBlock>, RimMindError>> PlanAsync(
        IAgentInfo agent,
        CancellationToken ct = default);
}
