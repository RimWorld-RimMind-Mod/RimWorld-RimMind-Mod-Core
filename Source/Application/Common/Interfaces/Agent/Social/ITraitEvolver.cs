using RimMind.Domain.Agent.Social;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Common.Interfaces.Agent.Social;

/// <summary>
/// Abstraction for applying trait evolution to pawns in the Verse game engine.
/// Decouples Presentation layer (ProactiveBehaviorExecutor) from Infrastructure (VerseTraitEvolver).
/// </summary>
public interface ITraitEvolver
{
    Result<TraitEvolutionRecord, RimMindError> ApplyTraitEvolution(int pawnId, TraitEvolutionRecord record);
}
