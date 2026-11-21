using System;
using RimWorld;
using Verse;

namespace RimMind.Presentation.Api
{
    /// <summary>
    /// Counts map-wide available (unforbidden, unfogged) resources and human-edible food nutrition,
    /// combining stockpile counts (<c>map.resourceCounter</c>) with loose spawned unforbidden items on the ground.
    /// </summary>
    public static class MapResourceCounter
    {
        /// <summary>
        /// Returns the maximum of stockpile count and spawned unforbidden, unfogged items of <paramref name="def"/> on <paramref name="map"/>.
        /// </summary>
        public static int GetAvailableCount(Map? map, ThingDef? def)
        {
            if (map == null || def == null)
                return 0;

            int counted = 0;
            try
            {
                counted = map.resourceCounter?.GetCount(def) ?? 0;
                var spawnedList = map.listerThings?.ThingsOfDef(def);
                if (spawnedList != null && spawnedList.Count > 0)
                {
                    int unforbiddenTotal = 0;
                    for (int i = 0; i < spawnedList.Count; i++)
                    {
                        var t = spawnedList[i];
                        if (t != null && t.Spawned && !t.Destroyed && !t.Position.Fogged(map) && !t.IsForbidden(Faction.OfPlayer))
                        {
                            unforbiddenTotal += t.stackCount;
                        }
                    }

                    if (unforbiddenTotal > counted)
                        counted = unforbiddenTotal;
                }
            }
            catch
            {
                // Best-effort count when map state is partially initialized.
            }

            return counted;
        }

        /// <summary>
        /// Returns the maximum of <c>map.resourceCounter.TotalHumanEdibleNutrition</c> and loose spawned unforbidden,
        /// unfogged human-edible food items (excluding corpses, pawns, and drugs) on <paramref name="map"/>.
        /// </summary>
        public static float GetAvailableFoodNutrition(Map? map)
        {
            if (map == null)
                return 0f;

            float counted = 0f;
            try
            {
                counted = map.resourceCounter?.TotalHumanEdibleNutrition ?? 0f;
                var foodThings = map.listerThings?.ThingsInGroup(ThingRequestGroup.FoodSourceNotPlantOrTree);
                if (foodThings != null && foodThings.Count > 0)
                {
                    float unforbiddenTotal = 0f;
                    for (int i = 0; i < foodThings.Count; i++)
                    {
                        var t = foodThings[i];
                        if (t == null || !t.Spawned || t.Destroyed || t is Corpse || t is Pawn)
                            continue;
                        if (t.Position.Fogged(map) || t.IsForbidden(Faction.OfPlayer))
                            continue;

                        var ingestible = t.def?.ingestible;
                        if (ingestible == null || !t.def!.IsNutritionGivingIngestible || !ingestible.HumanEdible)
                            continue;
                        if (ingestible.drugCategory != DrugCategory.None || ingestible.preferability < FoodPreferability.RawBad)
                            continue;

                        float nut = t.GetStatValue(StatDefOf.Nutrition);
                        if (nut > 0f)
                        {
                            unforbiddenTotal += nut * Math.Max(1, t.stackCount);
                        }
                    }

                    if (unforbiddenTotal > counted)
                        counted = unforbiddenTotal;
                }
            }
            catch
            {
                // Best-effort count when map state is partially initialized.
            }

            return counted;
        }
    }
}
