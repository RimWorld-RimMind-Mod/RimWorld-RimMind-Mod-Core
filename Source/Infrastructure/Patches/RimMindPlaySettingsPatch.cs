using HarmonyLib;
using RimMind.Application.Common.Interfaces.Extension;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Infrastructure.UI;
using RimMind.Presentation.Api;
using RimMind.Presentation.Runtime.Services;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimMind.Infrastructure.Patches
{
    [HarmonyPatch(typeof(PlaySettings), nameof(PlaySettings.DoPlaySettingsGlobalControls))]
    [StaticConstructorOnStartup]
    public static class RimMindPlaySettingsPatch
    {
        private static readonly Texture2D Icon =
            ContentFinder<Texture2D>.Get("UI/RimMind/Icon", reportFailure: false) ?? BaseContent.BadTex;
        private static readonly RuntimeServiceRef<IExtensionRegistry<IToggleBehavior>> ToggleRegistry =
            RuntimeServiceRef<IExtensionRegistry<IToggleBehavior>>.Optional();
        private static readonly RuntimeServiceRef<ISettingsProvider> SettingsProvider =
            RuntimeServiceRef<ISettingsProvider>.Optional();

        private static bool _iconState;

        public static void Postfix(WidgetRow row, bool worldView)
        {
            if (worldView || row == null) return;

            RuntimeServiceScope runtimeScope = RuntimeServiceHub.Shared.Capture();
            var toggleRegistry = ToggleRegistry.ResolveOptional(runtimeScope);
            // Sync icon visual state from toggle registry before rendering.
            // ToggleableIcon flips _iconState via ref on click, so we compare
            // prev vs post to detect user interaction, then route by modifier.
            _iconState = IsAnyToggleActive(toggleRegistry);

            bool prev = _iconState;
            row.ToggleableIcon(
                ref _iconState,
                Icon,
                "RimMind.Presentation.Toggle.Tooltip".Translate(),
                SoundDefOf.Mouseover_ButtonToggle);

            if (_iconState == prev) return;

            bool shift = Event.current.shift;
            bool control = Event.current.control;

            if (control)
            {
                Find.WindowStack.Add(Window_RimMindHub.OpenAIRequests());
            }
            else if (shift)
            {
                OpenSettings(runtimeScope);
            }
            else
            {
                ToggleCoreOverlay(toggleRegistry);
            }

            // Reset visual state to match actual toggle state (not the widget flip)
            _iconState = IsAnyToggleActive(toggleRegistry);
        }

        private static bool IsAnyToggleActive(IExtensionRegistry<IToggleBehavior>? registry)
        {
            if (registry == null) return false;
            foreach (var toggle in registry.All)
            {
                if (toggle.IsActive) return true;
            }
            return false;
        }

        private static void ToggleCoreOverlay(IExtensionRegistry<IToggleBehavior>? registry)
        {
            if (registry == null) return;
            foreach (var toggle in registry.All)
            {
                if (toggle.Id == "request_overlay")
                {
                    toggle.Toggle();
                    return;
                }
            }
        }

        private static void OpenSettings(RuntimeServiceScope runtimeScope)
        {
            var sp = SettingsProvider.ResolveOptional(runtimeScope);
            if (sp != null)
            {
                Find.WindowStack.Add(new Window_RimMindSettings());
            }
        }
    }
}
