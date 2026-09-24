using HarmonyLib;
using RimMind.Presentation.Runtime;
using Verse;

namespace RimMind.Infrastructure.Patches
{
    [HarmonyPatch(typeof(GenScene), nameof(GenScene.GoToMainMenu))]
    internal static class GenScene_GameLifecyclePatch
    {
        [HarmonyPrefix]
        private static void StopGameServicesBeforeReturningToMainMenu()
        {
            RimMindRuntimeGameComponent.StopGameServices();
        }
    }
}
