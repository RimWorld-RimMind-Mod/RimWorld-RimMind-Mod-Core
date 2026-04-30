using HarmonyLib;
using RimMind.Core.Perception;
using Verse;

namespace RimMind.Core.Patch
{
    [HarmonyPatch(typeof(HediffSet), "AddDirect")]
    public static class PerceptionBridge_PatchHealth
    {
        static void Postfix(HediffSet __instance, Hediff hediff)
        {
            if (hediff == null || hediff.def == null) return;
            if (hediff.def.hediffClass == null) return;
            bool isHarmful = hediff.def.IsAddiction || hediff.def.tendable || hediff.def.chronic
                || hediff.def.makesSickThought;
            if (!isHarmful && hediff.PainFactor <= 1f && hediff.Severity <= 0.1f) return;
            if (__instance.pawn == null || __instance.pawn.Dead) return;

            float severity = hediff.Severity;
            if (severity <= 0.5f && !__instance.pawn.IsHashIntervalTick(60)) return;

            float importance = severity > 0.5f ? 0.9f : 0.7f;
            string content = $"HediffAdded:{hediff.def.defName}(severity:{severity:F1})";
            PerceptionBridge.PublishPerceptionForPawn(__instance.pawn, "damage", content, importance);
        }
    }
}
