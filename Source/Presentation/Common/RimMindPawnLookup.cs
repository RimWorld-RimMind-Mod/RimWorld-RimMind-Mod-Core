using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimMind.Presentation.Api
{
    /// <summary>
    /// Centralized lookup and eligibility policy for Pawns across Core and submodules.
    /// Unifies pawn resolution logic and colonist validity checks.
    /// </summary>
    public static class RimMindPawnLookup
    {
        /// <summary>
        /// Finds a pawn across world pawns, current map, all maps, and caravans by String ThingID.
        /// </summary>
        public static Pawn? FindPawnById(string? pawnId)
        {
            if (string.IsNullOrEmpty(pawnId)) return null;

            var pawn = Find.WorldPawns?.AllPawnsAliveOrDead?
                .FirstOrDefault(p => p.ThingID == pawnId);
            if (pawn != null) return pawn;

            if (Find.Maps != null)
            {
                foreach (var map in Find.Maps)
                {
                    pawn = map.mapPawns?.AllPawns?
                        .FirstOrDefault(p => p.ThingID == pawnId);
                    if (pawn != null) return pawn;
                }
            }

            if (Find.WorldObjects?.Caravans != null)
            {
                foreach (var caravan in Find.WorldObjects.Caravans)
                {
                    pawn = caravan.PawnsListForReading?
                        .FirstOrDefault(p => p.ThingID == pawnId);
                    if (pawn != null) return pawn;
                }
            }

            return null;
        }

        /// <summary>
        /// Finds a pawn by numeric thingIDNumber.
        /// Checks world pawns first, then current map free colonists and all maps.
        /// </summary>
        public static Pawn? FindPawnByNumber(int thingIDNumber)
        {
            if (thingIDNumber <= 0) return null;

            var pawn = Find.WorldPawns?.AllPawnsAlive?
                .FirstOrDefault(p => p.thingIDNumber == thingIDNumber);
            if (pawn != null) return pawn;

            pawn = Find.CurrentMap?.mapPawns?.FreeColonists?
                .FirstOrDefault(p => p.thingIDNumber == thingIDNumber);
            if (pawn != null) return pawn;

            if (Find.Maps != null)
            {
                foreach (var map in Find.Maps)
                {
                    pawn = map.mapPawns?.FreeColonists?
                        .FirstOrDefault(p => p.thingIDNumber == thingIDNumber);
                    if (pawn != null) return pawn;
                }
            }

            return null;
        }

        /// <summary>
        /// Checks if a pawn is an eligible active colonist (alive, colonist, conscious, not in mental break).
        /// </summary>
        public static bool IsEligibleColonist(Pawn? pawn)
        {
            if (pawn == null) return false;
            if (pawn.Dead) return false;
            if (!pawn.IsColonist) return false;
            if (pawn.Downed) return false;
            if (pawn.MentalState != null) return false;
            return true;
        }

        /// <summary>
        /// Returns all eligible living colonists on the given map (or current map if not specified).
        /// </summary>
        public static IEnumerable<Pawn> GetEligibleColonists(Map? map = null)
        {
            var targetMap = map ?? Find.CurrentMap;
            if (targetMap?.mapPawns?.FreeColonists == null) yield break;

            foreach (var pawn in targetMap.mapPawns.FreeColonists)
            {
                if (IsEligibleColonist(pawn))
                {
                    yield return pawn;
                }
            }
        }
    }
}
