using System;
using RimMind.Application.Common.Interfaces.Agent.Psychology;
using RimMind.Domain.Agent.Psychology;
using RimMind.Domain.ValueObjects;
using RimMind.Presentation.Api;
using Verse;
using RimWorld;

namespace RimMind.Infrastructure.Psychology
{
    /// <summary>
    /// IThoughtInjector implementation that injects AI-generated dynamic thoughts
    /// into RimWorld's thought system via Thought_RimMindDynamic.
    /// </summary>
    /// <remarks>
    /// Thoughts are a write action on an LLM-supplied pawn id, so the target must be an active
    /// spawned colonist (downed is allowed; prisoners, guests, enemies, dead, kidnapped and
    /// off-map pawns are rejected). Off the main thread the target is pre-validated from the
    /// lookup cache and the Verse side effect is marshalled through <see cref="RimMindAPI.MainThread"/>,
    /// where the target is validated again.
    /// </remarks>
    public sealed class VerseThoughtInjector : IThoughtInjector
    {
        private const string DynamicThoughtDefName = "RimMind_DynamicThought";

        public Result<RimMindDynamicThought, RimMindError> InjectThought(
            int pawnId, string thoughtText, float moodOffset, int durationTicks, string source)
        {
            if (string.IsNullOrWhiteSpace(thoughtText))
                return Result<RimMindDynamicThought, RimMindError>.Err(
                    RimMindErrors.Internal("Thought text cannot be empty"));

            var pawn = FindTargetColonist(pawnId);
            if (pawn == null)
                return Result<RimMindDynamicThought, RimMindError>.Err(
                    RimMindErrors.PawnNotFound(pawnId));

            var dynamicThought = new RimMindDynamicThought
            {
                ThoughtText = thoughtText,
                MoodOffset = moodOffset,
                CreatedTick = UnityData.IsInMainThread ? Find.TickManager?.TicksGame ?? 0 : 0,
                DurationTicks = durationTicks,
                Source = source
            };

            if (!UnityData.IsInMainThread)
            {
                RimMindAPI.MainThread.Post(() =>
                {
                    var target = FindTargetColonist(pawnId);
                    if (target == null) return;
                    var result = GainThought(target, thoughtText, moodOffset, durationTicks);
                    if (result.IsErr)
                        Log.Warning($"[RimMind-Core] Deferred dynamic thought for pawn {pawnId} failed: {result.Error.Message}");
                });
                return Result<RimMindDynamicThought, RimMindError>.Ok(dynamicThought);
            }

            var gained = GainThought(pawn, thoughtText, moodOffset, durationTicks);
            return gained.IsOk
                ? Result<RimMindDynamicThought, RimMindError>.Ok(dynamicThought)
                : Result<RimMindDynamicThought, RimMindError>.Err(gained.Error);
        }

        private static Result<bool, RimMindError> GainThought(Pawn pawn, string thoughtText, float moodOffset, int durationTicks)
        {
            try
            {
                var thoughtDef = DefDatabase<ThoughtDef>.GetNamedSilentFail(DynamicThoughtDefName);
                if (thoughtDef == null)
                    return Result<bool, RimMindError>.Err(RimMindErrors.InvalidDefName(DynamicThoughtDefName));

                var memories = pawn.needs?.mood?.thoughts?.memories;
                if (memories == null)
                    return Result<bool, RimMindError>.Err(RimMindErrors.Internal("Pawn has no mood/memory tracker"));

                var thought = (Thought_RimMindDynamic)ThoughtMaker.MakeThought(thoughtDef);
                thought.MoodOffsetValue = moodOffset;
                thought.ThoughtText = thoughtText;
                thought.CustomDurationTicks = durationTicks;

                memories.TryGainMemory(thought);
                return Result<bool, RimMindError>.Ok(true);
            }
            catch (Exception ex)
            {
                return Result<bool, RimMindError>.Err(
                    RimMindErrors.Internal($"Failed to inject thought: {ex.Message}", ex));
            }
        }

        private static Pawn? FindTargetColonist(int pawnId)
        {
            var pawn = RimMindPawnLookup.FindPawnByNumber(pawnId);
            return RimMindPawnLookup.IsActiveSpawnedColonist(pawn) ? pawn : null;
        }
    }
}
