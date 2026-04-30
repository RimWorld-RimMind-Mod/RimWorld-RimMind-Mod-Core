using HarmonyLib;
using RimMind.Core.Npc;
using Verse;

namespace RimMind.Core.Patch
{
    [HarmonyPatch(typeof(MapGenerator), "GenerateMap")]
    public static class Patch_MapGenerator_GenerateMap
    {
        static void Postfix(Map __result)
        {
            if (__result == null) return;

            var comp = __result.GetComponent<MapNpcComponent>();
            if (comp == null)
            {
                __result.components.Add(new MapNpcComponent(__result));
            }
        }
    }

}
