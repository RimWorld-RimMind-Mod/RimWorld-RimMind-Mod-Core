using System.Collections.Generic;
using HarmonyLib;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Models.Agent;
using RimMind.Infrastructure.UI;
using RimMind.Presentation.Api;
using RimMind.Presentation.Runtime.Services;
using UnityEngine;
using Verse;
using RimWorld;

namespace RimMind.Infrastructure.Patches
{
    [HarmonyPatch(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.GetOptions))]
    internal static class FloatMenu_InnerVoice
    {
        private static readonly RuntimeServiceRef<IAgentIdentityProvider> IdentityProvider =
            RuntimeServiceRef<IAgentIdentityProvider>.Optional();

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

        private static void TryAddInnerVoiceOption(Pawn? pawn, List<FloatMenuOption> opts)
        {
            if (opts == null) return;
            if (!RimMindPawnLookup.IsActiveSpawnedColonist(pawn)) return;

            // Only show for pawns that have a PawnAgent
            var identityProvider = IdentityProvider.ValueOrDefault;
            var identity = identityProvider?.GetAgentIdentity(pawn!);
            if (identity == null) return;

            var label = RimMindText.TrOr("RimMind.InnerVoice.Inject", "Inner Voice ({0})", pawn!.LabelShort);
            var option = new FloatMenuOption(label, () =>
            {
                var dialog = new Dialog_RimMindInnerVoice(pawn, identity);
                Find.WindowStack.Add(dialog);
            }, MenuOptionPriority.Default);

            opts.Add(option);
        }
    }
}
