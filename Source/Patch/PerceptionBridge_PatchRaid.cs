using HarmonyLib;
using RimMind.Core.Perception;
using RimWorld;
using Verse;

namespace RimMind.Core.Patch
{
    [HarmonyPatch(typeof(IncidentWorker_Raid), "TryExecuteWorker")]
    public static class PerceptionBridge_PatchRaid
    {
        static void Postfix(IncidentWorker_Raid __instance, IncidentParms parms, ref bool __result)
        {
            if (!__result) return;
            if (parms?.target is not Map map) return;

            string raidType = __instance.def?.defName ?? "Raid";
            string content = $"RaidStarted:{raidType}";
            PerceptionBridge.PublishBroadcast("raid", content, 1.0f, map);
        }
    }
}
