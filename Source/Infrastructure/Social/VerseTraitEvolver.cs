using System.Linq;
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
        var pawn = FindPawn(pawnId);
        if (pawn == null) return Result<TraitEvolutionRecord, RimMindError>.Err(
            RimMindErrors.PawnNotFound(pawnId));

        var traitDef = DefDatabase<TraitDef>.GetNamedSilentFail(record.TraitDefName);
        if (traitDef == null) return Result<TraitEvolutionRecord, RimMindError>.Err(
            RimMindErrors.InvalidDefName(record.TraitDefName));

        if (record.Kind == TraitEvolutionKind.Gained)
        {
            if (!pawn.story.traits.HasTrait(traitDef))
                pawn.story.traits.GainTrait(new Trait(traitDef));
        }
        else
        {
            var existing = pawn.story.traits.GetTrait(traitDef);
            if (existing != null)
                pawn.story.traits.RemoveTrait(existing);
        }

        return Result<TraitEvolutionRecord, RimMindError>.Ok(record);
    }

    private static Pawn? FindPawn(int pawnId)
    {
        var cached = Presentation.Api.RimMindPawnLookup.FindPawnByNumber(pawnId);
        if (cached != null) return cached;

        if (UnityData.IsInMainThread && Find.Maps != null)
        {
            foreach (var map in Find.Maps)
            {
                var pawn = map.mapPawns?.FreeColonistsSpawned?.FirstOrDefault(p => p.thingIDNumber == pawnId);
                if (pawn != null && Presentation.Api.RimMindPawnLookup.IsActiveSpawnedColonist(pawn))
                {
                    Presentation.Api.RimMindPawnLookup.CachePawn(pawn);
                    return pawn;
                }
            }
        }

        return null;
    }
}
