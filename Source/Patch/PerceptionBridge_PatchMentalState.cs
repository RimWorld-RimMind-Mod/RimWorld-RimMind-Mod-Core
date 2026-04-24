using HarmonyLib;
using RimMind.Core.Perception;
using Verse;
using Verse.AI;

namespace RimMind.Core.Patch
{
    [HarmonyPatch(typeof(MentalStateHandler), "TryStartMentalState")]
    public static class PerceptionBridge_PatchMentalState
    {
        static void Postfix(MentalStateHandler __instance, MentalStateDef stateDef, bool __result)
        {
            if (!__result) return;
            var pawn = Traverse.Create(__instance).Field("pawn").GetValue<Pawn>();
            if (pawn == null || pawn.Dead) return;

            string content = $"MentalBreak:{pawn.LabelShortCap}({stateDef?.defName ?? "unknown"})";
            PerceptionBridge.PublishPerceptionForPawn(pawn, "mental_break", content, 0.9f);
            PerceptionBridge.PublishBroadcast("mental_break", content, 0.6f, pawn.Map);
        }
    }
}
