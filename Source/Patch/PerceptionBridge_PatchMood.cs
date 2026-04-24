using System.Collections.Generic;
using HarmonyLib;
using RimMind.Core.Perception;
using RimWorld;
using Verse;

namespace RimMind.Core.Patch
{
    [HarmonyPatch(typeof(Need_Mood), "NeedInterval")]
    public static class PerceptionBridge_PatchMood
    {
        private static readonly Dictionary<int, float> _lastMoodLevel = new Dictionary<int, float>();

        static void Postfix(Need_Mood __instance)
        {
            var pawn = Traverse.Create(__instance).Field("pawn").GetValue<Pawn>();
            if (pawn == null) return;

            int pawnId = pawn.thingIDNumber;

            if (pawn.Dead)
            {
                _lastMoodLevel.Remove(pawnId);
                return;
            }

            if (!pawn.IsHashIntervalTick(150)) return;

            float currentLevel = __instance.CurLevel;

            if (!_lastMoodLevel.TryGetValue(pawnId, out float lastLevel))
            {
                _lastMoodLevel[pawnId] = currentLevel;
                return;
            }

            float drop = lastLevel - currentLevel;
            if (drop > 0.1f)
            {
                string content = $"MoodDrop:{currentLevel:F2}(drop:{drop:F2})";
                PerceptionBridge.PublishPerceptionForPawn(pawn, "mood_drop", content, 0.6f);
            }

            if (currentLevel < 0.15f && lastLevel >= 0.15f)
            {
                string content = $"MoodCritical:{currentLevel:F2}";
                PerceptionBridge.PublishPerceptionForPawn(pawn, "mood_critical", content, 0.9f);
            }

            _lastMoodLevel[pawnId] = currentLevel;
        }
    }
}
