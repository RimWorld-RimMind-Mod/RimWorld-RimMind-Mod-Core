using System.Collections.Generic;
using HarmonyLib;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Models.Agent;
using RimMind.Infrastructure.UI;
using RimMind.Presentation.Runtime.Services;
using UnityEngine;
using Verse;
using RimWorld;

namespace RimMind.Infrastructure.Patches
{
#if V1_5
    [HarmonyPatch(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.ChoicesAtFor))]
#else
    [HarmonyPatch(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.GetOptions))]
#endif
    internal static class FloatMenu_InnerVoice
    {
        private static readonly RuntimeServiceRef<IAgentIdentityProvider> IdentityProvider =
            RuntimeServiceRef<IAgentIdentityProvider>.Optional();

#if V1_5
        [HarmonyPostfix]
        internal static void Postfix(Vector3 clickPos, Pawn pawn, ref List<FloatMenuOption> __result)
        {
            TryAddInnerVoiceOption(pawn, __result);
        }
#else
        [HarmonyPostfix]
        internal static void Postfix(
            List<Pawn> selectedPawns,
            Vector3 clickPos,
            FloatMenuContext context,
            ref List<FloatMenuOption> __result)
        {
            Pawn? pawn = (selectedPawns is { Count: 1 }) ? selectedPawns[0] : null;
            TryAddInnerVoiceOption(pawn, __result);
        }
#endif

        private static void TryAddInnerVoiceOption(Pawn? pawn, List<FloatMenuOption> opts)
        {
            if (opts == null) return;
            if (pawn == null || !pawn.Spawned || pawn.Dead) return;

            // Only show for pawns that have a PawnAgent
            var identityProvider = IdentityProvider.ValueOrDefault;
            var identity = identityProvider?.GetAgentIdentity(pawn);
            if (identity == null) return;

            var label = "RimMind.InnerVoice.Inject".Translate(pawn.LabelShort);
            var option = new FloatMenuOption(label, () =>
            {
                var dialog = new Dialog_RimMindInnerVoice(pawn, identity);
                Find.WindowStack.Add(dialog);
            }, MenuOptionPriority.Default);

            opts.Add(option);
        }
    }
}
