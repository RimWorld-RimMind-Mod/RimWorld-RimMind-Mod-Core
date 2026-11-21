using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimMind.Presentation.Api
{
    public static partial class RimMindPawnLookup
    {
        /// <summary>
        /// Collects the numeric <c>thingIDNumber</c> of every colony-relevant pawn whose persistent data
        /// (memory, personality, etc.) should be retained across pruning passes:
        /// alive colonists (across maps, caravans, and travelling transporters), prisoners/slaves on maps,
        /// colony/prisoner/slave corpses on maps, and pawns held in any faction's kidnapped list.
        /// Must be called on the main thread.
        /// </summary>
        public static HashSet<int> CollectRetainedColonyPawnIds()
        {
            var retained = new HashSet<int>();
            if (!UnityData.IsInMainThread)
                return retained;

            try
            {
                var aliveColonists = PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_Colonists;
                if (aliveColonists != null)
                {
                    for (int i = 0; i < aliveColonists.Count; i++)
                    {
                        var p = aliveColonists[i];
                        if (p != null && p.thingIDNumber > 0 && !p.Destroyed)
                            retained.Add(p.thingIDNumber);
                    }
                }
            }
            catch
            {
                // Ignore if PawnsFinder is unavailable during early init/shutdown.
            }

            try
            {
                var maps = Find.Maps;
                if (maps != null)
                {
                    for (int m = 0; m < maps.Count; m++)
                    {
                        var map = maps[m];
                        if (map == null) continue;

                        var mapPawns = map.mapPawns?.AllPawns;
                        if (mapPawns != null)
                        {
                            for (int i = 0; i < mapPawns.Count; i++)
                            {
                                var p = mapPawns[i];
                                if (p != null && p.thingIDNumber > 0 && !p.Destroyed &&
                                    (p.IsColonist || p.IsPrisonerOfColony || p.IsSlaveOfColony))
                                {
                                    retained.Add(p.thingIDNumber);
                                }
                            }
                        }

                        var corpses = map.listerThings?.ThingsInGroup(ThingRequestGroup.Corpse);
                        if (corpses != null)
                        {
                            for (int i = 0; i < corpses.Count; i++)
                            {
                                if (corpses[i] is Corpse corpse &&
                                    corpse.InnerPawn is Pawn inner &&
                                    inner.thingIDNumber > 0 &&
                                    !inner.Destroyed &&
                                    (inner.IsColonist || inner.IsPrisonerOfColony || inner.IsSlaveOfColony))
                                {
                                    retained.Add(inner.thingIDNumber);
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // Ignore transient map state errors.
            }

            try
            {
                var factions = Find.FactionManager?.AllFactionsListForReading;
                if (factions != null)
                {
                    for (int i = 0; i < factions.Count; i++)
                    {
                        var kidnapped = factions[i]?.kidnapped?.KidnappedPawnsListForReading;
                        if (kidnapped == null) continue;
                        for (int k = 0; k < kidnapped.Count; k++)
                        {
                            var p = kidnapped[k];
                            if (p != null && p.thingIDNumber > 0 && !p.Destroyed)
                                retained.Add(p.thingIDNumber);
                        }
                    }
                }
            }
            catch
            {
                // Ignore if FactionManager is unavailable.
            }

            return retained;
        }
    }
}
