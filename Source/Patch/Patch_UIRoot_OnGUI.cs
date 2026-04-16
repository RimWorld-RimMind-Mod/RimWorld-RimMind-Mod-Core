using HarmonyLib;
using RimMind.Core.UI;
using Verse;

namespace RimMind.Core.Patch
{
    [HarmonyPatch(typeof(UIRoot), "UIRootOnGUI")]
    public static class Patch_UIRoot_OnGUI
    {
        static void Postfix()
        {
            RequestOverlay.OnGUI();
        }
    }
}
