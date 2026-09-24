using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimMind.Infrastructure.Verse;
using RimWorld;
using Verse;

namespace RimMind.Infrastructure.Patches
{
    [HarmonyPatch(typeof(ThingDef), nameof(ThingDef.ResolveReferences))]
    public static class AddAgentCompToHumanlikePatch
    {
        [HarmonyPostfix]
        public static void Postfix(ThingDef __instance)
        {
            if (__instance.race?.intelligence != Intelligence.Humanlike) return;

            __instance.comps ??= new List<CompProperties>();
            if (!__instance.comps.Any(c => c is CompProperties_PawnAgent))
                __instance.comps.Add(new CompProperties_PawnAgent());

            __instance.inspectorTabs ??= new List<System.Type>();
            if (!__instance.inspectorTabs.Contains(typeof(ITab_Pawn_Agent)))
                __instance.inspectorTabs.Add(typeof(ITab_Pawn_Agent));
        }
    }
}
