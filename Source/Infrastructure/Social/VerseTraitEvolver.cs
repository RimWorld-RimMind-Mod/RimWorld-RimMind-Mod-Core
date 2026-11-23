using RimMind.Application.Common.Interfaces.Agent.Social;
using RimMind.Domain.Agent.Social;
using RimMind.Domain.Enums;
using RimMind.Domain.ValueObjects;
using RimWorld;
using Verse;

namespace RimMind.Infrastructure.Social;

public sealed class VerseTraitEvolver : ITraitEvolver
{
    public Result<TraitEvolutionRecord, RimMindError> ApplyTraitEvolution(int pawnId, TraitEvolutionRecord record)
    {
        var pawn = FindEligiblePawn(pawnId);
        if (pawn == null) return Result<TraitEvolutionRecord, RimMindError>.Err(
            RimMindErrors.PawnNotFound(pawnId));

        var traits = pawn.story?.traits;
        if (traits == null) return Result<TraitEvolutionRecord, RimMindError>.Err(
            RimMindErrors.PawnNotFound(pawnId));

        var traitDef = DefDatabase<TraitDef>.GetNamedSilentFail(record.TraitDefName);
        if (traitDef == null) return Result<TraitEvolutionRecord, RimMindError>.Err(
            RimMindErrors.InvalidDefName(record.TraitDefName));

        if (record.Kind == TraitEvolutionKind.Gained)
        {
            if (!traits.HasTrait(traitDef))
                traits.GainTrait(new Trait(traitDef));
        }
        else
        {
            var existing = traits.GetTrait(traitDef);
            if (existing != null)
                traits.RemoveTrait(existing);
        }

        return Result<TraitEvolutionRecord, RimMindError>.Ok(record);
    }

    private static Pawn? FindEligiblePawn(int pawnId)
    {
        var pawn = Presentation.Api.RimMindPawnLookup.FindPawnByNumber(pawnId);
        if (pawn != null && Presentation.Api.RimMindPawnLookup.IsActiveSpawnedColonist(pawn))
            return pawn;
        return null;
    }
}
