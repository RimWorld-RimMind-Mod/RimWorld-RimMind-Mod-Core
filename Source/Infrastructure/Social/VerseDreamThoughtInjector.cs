using RimMind.Application.Common.Interfaces.Agent.Psychology;
using RimMind.Application.Common.Interfaces.Agent.Social;
using RimMind.Domain.Agent.Social;
using RimMind.Domain.ValueObjects;

namespace RimMind.Infrastructure.Social;

public sealed class VerseDreamThoughtInjector : IDreamThoughtInjector
{
    private readonly IThoughtInjector _thoughtInjector;

    public VerseDreamThoughtInjector(IThoughtInjector thoughtInjector)
    {
        _thoughtInjector = thoughtInjector;
    }

    public Result<DreamEntry, RimMindError> InjectDreamThought(int pawnId, DreamEntry dream)
    {
        var thoughtResult = _thoughtInjector.InjectThought(
            pawnId,
            $"[Dream] {dream.DreamContent}",
            dream.MoodImpact * 5f,
            60000,
            $"Dream:{dream.DreamType}");

        return thoughtResult.Match(
            ok => Result<DreamEntry, RimMindError>.Ok(dream),
            err => Result<DreamEntry, RimMindError>.Err(err));
    }
}
