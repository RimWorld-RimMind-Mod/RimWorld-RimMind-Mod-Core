using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Domain.Agent.Reflection;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Common.Interfaces.Agent.Reflection;

public interface IReflectionStrategy
{
    bool ShouldReflect(IAgentInfo agent);

    Task<Result<IReadOnlyList<ReflectionEntry>, RimMindError>> ReflectAsync(
        IAgentInfo agent,
        CancellationToken ct = default);
}
