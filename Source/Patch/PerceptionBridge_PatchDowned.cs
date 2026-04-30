using System.Collections.Generic;
using HarmonyLib;
using RimMind.Core.Comps;
using RimMind.Core.Perception;
using Verse;

namespace RimMind.Core.Patch
{
    [HarmonyPatch(typeof(Pawn), "get_Downed")]
    public static class PerceptionBridge_PatchDowned
    {
        private static readonly HashSet<int> _wasDowned = new HashSet<int>();

        static void Postfix(Pawn __instance, ref bool __result)
        {
            if (__instance == null) return;

            int pawnId = __instance.thingIDNumber;

            if (__instance.Dead)
            {
                _wasDowned.Remove(pawnId);
                return;
            }

            var comp = CompPawnAgent.GetComp(__instance);
            if (comp?.Agent?.IsActive != true) return;

            bool wasDown = _wasDowned.Contains(pawnId);
            if (__result && !wasDown)
            {
                _wasDowned.Add(pawnId);
                string content = $"PawnDowned:{__instance.LabelShortCap}";
                PerceptionBridge.PublishPerceptionForPawn(__instance, "pawn_downed", content, 0.9f);
                PerceptionBridge.PublishBroadcast("pawn_downed", content, 0.7f, __instance.Map);
            }
            else if (!__result && wasDown)
            {
                _wasDowned.Remove(pawnId);
            }
        }
    }
}
