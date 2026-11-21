using RimMind.Domain.ValueObjects;
using Verse;

namespace RimMind.Presentation.Api
{
    /// <summary>
    /// Resolves a target <see cref="Map"/> and active colonist <see cref="Pawn"/> from optional pawn/map IDs,
    /// enforcing RimMind's pawn liveness policy.
    /// </summary>
    public static class ActorMapResolver
    {
        /// <summary>
        /// Resolves a map by explicit <paramref name="mapId"/>, otherwise by <paramref name="pawnId"/>'s current map,
        /// otherwise <c>Find.AnyPlayerHomeMap</c> / <c>Find.CurrentMap</c>.
        /// </summary>
        public static Map? ResolveMap(int? pawnId, int? mapId = null)
        {
            if (mapId.HasValue)
            {
                var maps = Find.Maps;
                if (maps != null)
                {
                    for (int i = 0; i < maps.Count; i++)
                    {
                        if (maps[i]?.uniqueID == mapId.Value)
                            return maps[i];
                    }
                }
                return null;
            }

            if (pawnId.HasValue && pawnId.Value > 0)
            {
                var pawn = RimMindPawnLookup.FindPawnByNumber(pawnId.Value);
                if (pawn?.Map != null)
                    return pawn.Map;
            }

            return Find.AnyPlayerHomeMap ?? Find.CurrentMap;
        }

        /// <summary>
        /// Resolves an active spawned player colonist and its map. When <paramref name="allowDowned"/> is false,
        /// downed or mentally broken colonists are rejected with a descriptive <see cref="RimMindError"/>.
        /// </summary>
        public static bool ResolveColonistAndMap(
            int pawnId,
            bool allowDowned,
            out Pawn? pawn,
            out Map? map,
            out RimMindError? error,
            string? traceId = null)
        {
            pawn = RimMindPawnLookup.FindPawnByNumber(pawnId);
            if (pawn == null)
            {
                map = null;
                error = RimMindErrors.PawnNotFound(pawnId) with { TraceId = traceId };
                return false;
            }

            if (!RimMindPawnLookup.IsActiveSpawnedColonist(pawn))
            {
                map = null;
                error = RimMindErrors.PawnNotFound(pawnId) with
                {
                    Message = $"Pawn with ID {pawnId} is not an active spawned player colonist",
                    TraceId = traceId
                };
                pawn = null;
                return false;
            }

            if (!allowDowned && pawn.Downed)
            {
                map = null;
                error = RimMindErrors.PawnNotFound(pawnId) with
                {
                    Message = $"Pawn with ID {pawnId} is downed and cannot perform this action",
                    TraceId = traceId
                };
                pawn = null;
                return false;
            }

            if (!allowDowned && pawn.InMentalState)
            {
                map = null;
                error = RimMindErrors.PawnNotFound(pawnId) with
                {
                    Message = $"Pawn with ID {pawnId} is in a mental state ({pawn.MentalStateDef?.label}) and cannot be controlled",
                    TraceId = traceId
                };
                pawn = null;
                return false;
            }

            map = pawn.Map;
            if (map == null)
            {
                error = RimMindErrors.PawnNotFound(pawnId) with
                {
                    Message = $"Pawn with ID {pawnId} is not on an active map",
                    TraceId = traceId
                };
                pawn = null;
                return false;
            }

            error = null;
            return true;
        }
    }
}
