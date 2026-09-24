using System;
using RimMind.Application.Common.Interfaces.Agent.Psychology;
using RimMind.Domain.Agent.Psychology;
using RimMind.Domain.ValueObjects;
using Verse;
using RimWorld;

namespace RimMind.Infrastructure.Psychology
{
    /// <summary>
    /// IThoughtInjector implementation that injects AI-generated dynamic thoughts
    /// into RimWorld's thought system via Thought_RimMindDynamic.
    /// </summary>
    public sealed class VerseThoughtInjector : IThoughtInjector
    {
        public Result<RimMindDynamicThought, RimMindError> InjectThought(
            int pawnId, string thoughtText, float moodOffset, int durationTicks, string source)
        {
            if (string.IsNullOrWhiteSpace(thoughtText))
                return Result<RimMindDynamicThought, RimMindError>.Err(
                    RimMindErrors.Internal("Thought text cannot be empty"));

            var pawn = FindPawn(pawnId);
            if (pawn == null)
                return Result<RimMindDynamicThought, RimMindError>.Err(
                    RimMindErrors.PawnNotFound(pawnId));

            try
            {
                var thoughtDef = DefDatabase<ThoughtDef>.GetNamed("RimMind_DynamicThought");
                if (thoughtDef == null)
                    return Result<RimMindDynamicThought, RimMindError>.Err(
                        RimMindErrors.InvalidDefName("RimMind_DynamicThought"));

                var thought = (Thought_RimMindDynamic)ThoughtMaker.MakeThought(thoughtDef);
                thought.MoodOffsetValue = moodOffset;

                pawn.needs?.mood?.thoughts?.memories?.TryGainMemory(thought);

                var dynamicThought = new RimMindDynamicThought
                {
                    ThoughtText = thoughtText,
                    MoodOffset = moodOffset,
                    CreatedTick = Find.TickManager?.TicksGame ?? 0,
                    DurationTicks = durationTicks,
                    Source = source
                };

                return Result<RimMindDynamicThought, RimMindError>.Ok(dynamicThought);
            }
            catch (Exception ex)
            {
                return Result<RimMindDynamicThought, RimMindError>.Err(
                    RimMindErrors.Internal($"Failed to inject thought: {ex.Message}", ex));
            }
        }

        private static Pawn? FindPawn(int pawnId)
        {
            foreach (var map in Find.Maps)
            {
                var pawn = map.mapPawns?.AllPawns.FirstOrDefault(p => p.thingIDNumber == pawnId);
                if (pawn != null) return pawn;
            }
            var worldPawn = Find.WorldPawns?.AllPawnsAlive.FirstOrDefault(p => p.thingIDNumber == pawnId);
            return worldPawn;
        }
    }
}
