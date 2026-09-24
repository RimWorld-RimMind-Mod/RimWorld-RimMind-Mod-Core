using RimMind.Domain.Agent.Social;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Common.Interfaces.Agent.Social;

/// <summary>
/// Abstraction for injecting dream thoughts into the Verse game engine.
/// Decouples Presentation layer (ProactiveBehaviorExecutor) from Infrastructure (VerseDreamThoughtInjector).
/// </summary>
public interface IDreamThoughtInjector
{
    Result<DreamEntry, RimMindError> InjectDreamThought(int pawnId, DreamEntry dream);
}
