using HarmonyLib;
using RimMind.Core.UI;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimMind.Core.Patch
{
    [HarmonyPatch(typeof(PlaySettings), nameof(PlaySettings.DoPlaySettingsGlobalControls))]
    [StaticConstructorOnStartup]
    public static class AITogglePatch
    {
        private static readonly Texture2D Icon =
            ContentFinder<Texture2D>.Get("UI/RimMind/Icon", reportFailure: false) ?? BaseContent.BadTex;

        private static bool _iconState;

        public static void Postfix(WidgetRow row, bool worldView)
        {
            if (worldView || row == null) return;

            _iconState = RimMindAPI.IsAnyToggleActive();

            bool prev = _iconState;
            row.ToggleableIcon(
                ref _iconState,
                Icon,
                "RimMind.Core.Toggle.Tooltip".Translate(),
                SoundDefOf.Mouseover_ButtonToggle);

            if (_iconState == prev) return;

            bool shift = Event.current.shift;
            bool control = Event.current.control;

            if (shift)
            {
                if (!Find.WindowStack.IsOpen<Dialog_ModSettings>())
                    Find.WindowStack.Add(new Dialog_ModSettings(
                        LoadedModManager.GetMod<RimMindCoreMod>()));
            }
            else if (control)
            {
                if (!Find.WindowStack.IsOpen<Window_AIDebugLog>())
                    Find.WindowStack.Add(new Window_AIDebugLog());
            }
            else
            {
                RimMindAPI.ToggleAll();
            }

            _iconState = RimMindAPI.IsAnyToggleActive();
        }
    }
}
