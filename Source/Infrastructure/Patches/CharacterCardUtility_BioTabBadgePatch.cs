using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimMind.Presentation.UI;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimMind.Infrastructure.Patches
{
    /// <summary>
    /// Single unified Harmony transpiler on <see cref="CharacterCardUtility.DoTopStack"/> that renders all
    /// registered <see cref="IBioTabBadgeProvider"/> badges using a cached <see cref="FieldInfo"/> lookup.
    /// Replaces duplicate per-submodule transpilers in Personality and Memory.
    /// </summary>
    [HarmonyPatch(typeof(CharacterCardUtility), "DoTopStack")]
    internal static class CharacterCardUtility_BioTabBadgePatch
    {
        private static readonly FieldInfo? TmpStackElementsField =
            AccessTools.Field(typeof(CharacterCardUtility), "tmpStackElements");

        private static void AppendRegisteredBadges(Pawn? pawn)
        {
            if (pawn == null || TmpStackElementsField == null)
                return;

            var providers = BioTabBadgeRegistry.GetAll();
            if (providers.Count == 0)
                return;

            var tmpStackElements = TmpStackElementsField.GetValue(null) as List<GenUI.AnonymousStackElement>;
            if (tmpStackElements == null)
                return;

            for (int i = 0; i < providers.Count; i++)
            {
                var provider = providers[i];
                if (provider == null) continue;

                BioTabBadgeDescriptor? badge;
                try
                {
                    badge = provider.TryGetBadge(pawn);
                }
                catch (Exception ex)
                {
                    Log.Warning($"[RimMind-Core] BioTab badge provider '{provider.Id}' threw in TryGetBadge: {ex.Message}");
                    continue;
                }

                if (badge == null || string.IsNullOrEmpty(badge.Label))
                    continue;

                var capturedBadge = badge;
                string label = capturedBadge.Label;
                float totalW = Text.CalcSize(label).x + Math.Max(4f, capturedBadge.ExtraWidth);

                tmpStackElements.Add(new GenUI.AnonymousStackElement
                {
                    width = totalW,
                    drawer = rect =>
                    {
                        Widgets.DrawOptionBackground(rect, false);
                        Widgets.DrawHighlightIfMouseover(rect);

                        if (capturedBadge.TooltipProvider != null)
                        {
                            try
                            {
                                string tip = capturedBadge.TooltipProvider();
                                if (!string.IsNullOrEmpty(tip))
                                    TooltipHandler.TipRegion(rect, tip);
                            }
                            catch
                            {
                                // Never disrupt Bio tab rendering if tooltip provider throws.
                            }
                        }

                        TextAnchor prevAnchor = Text.Anchor;
                        Text.Anchor = TextAnchor.MiddleCenter;
                        Widgets.Label(rect, label);
                        Text.Anchor = prevAnchor;

                        if (capturedBadge.OnClick != null && Widgets.ButtonInvisible(rect))
                        {
                            try
                            {
                                capturedBadge.OnClick();
                            }
                            catch (Exception ex)
                            {
                                Log.Error($"[RimMind-Core] BioTab badge click handler failed: {ex}");
                            }
                        }
                    }
                });
            }
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo? anchor = AccessTools.Method(
                typeof(QuestUtility),
                nameof(QuestUtility.AppendInspectStringsFromQuestParts),
                new[] { typeof(Action<string, Quest>), typeof(ISelectable), typeof(int).MakeByRefType() });

            MethodInfo appendMethod = AccessTools.Method(
                typeof(CharacterCardUtility_BioTabBadgePatch),
                nameof(AppendRegisteredBadges));

            foreach (var instr in instructions)
            {
                yield return instr;
                if (anchor != null && instr.Calls(anchor))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Call, appendMethod);
                }
            }
        }
    }
}
