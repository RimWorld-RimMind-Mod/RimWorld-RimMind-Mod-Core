using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimMind.Core.Context;
using RimMind.Core.Internal;
using RimMind.Core.Npc;
using RimMind.Core.Settings;
using RimWorld;
using Verse;

namespace RimMind.Core.Flywheel
{
    public class FlywheelGameComponent : GameComponent
    {
        private int _lastAnalysisTick;

        private int AnalysisIntervalTicks =>
            RimMindCoreMod.Settings?.contextCalibrateInterval ?? 10000;

        public FlywheelGameComponent() : base() { }
        public FlywheelGameComponent(Game game) : base() { }

        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                RimMindAPI.Telemetry.Flush();
                var engine = RimMindAPI.GetContextEngine();
                engine?.GetEmbeddingSnapshotStore()?.Flush();
            }
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();
            int ticks = Find.TickManager?.TicksGame ?? 0;
            if (_lastAnalysisTick == 0)
                _lastAnalysisTick = ticks;
            if (ticks - _lastAnalysisTick >= AnalysisIntervalTicks)
            {
                _lastAnalysisTick = ticks;
                try
                {
                    RunPeriodicAnalysis();
                }
                catch (Exception ex) { Log.Warning($"[RimMind] Flywheel analysis failed: {ex.Message}"); }
            }
        }

        private void RunPeriodicAnalysis()
        {
            var records = RimMindAPI.Telemetry.GetRecentRecords(100);
            if (records == null || records.Count == 0) return;
            FlywheelRuleEngine.Analyze(records);
        }
    }

    [HarmonyPatch(typeof(Game), "FinalizeInit")]
    public static class FlywheelGameComponent_Register
    {
        static void Postfix(Game __instance)
        {
            if (!__instance.components.Any(c => c is FlywheelGameComponent))
                __instance.components.Add(new FlywheelGameComponent(__instance));

            if (!__instance.components.Any(c => c is FlywheelParameterStore))
            {
                var store = new FlywheelParameterStore(__instance);
                __instance.components.Add(store);
                store.FinalizeInit();
            }

            var engine = RimMindAPI.GetContextEngine();
            if (engine != null)
            {
                var scheduler = engine.GetScheduler();
                scheduler?.SubscribeParameterStore();
            }
        }
    }

    [HarmonyPatch(typeof(Game), "FinalizeInit")]
    public static class CoreGameComponent_Register
    {
        static void Postfix(Game __instance)
        {
            if (!__instance.components.Any(c => c is Internal.AIRequestQueue))
                __instance.components.Add(new Internal.AIRequestQueue(__instance));

            if (!__instance.components.Any(c => c is AIDebugLog))
                __instance.components.Add(new AIDebugLog(__instance));

            if (!__instance.components.Any(c => c is Npc.NpcManager))
                __instance.components.Add(new Npc.NpcManager(__instance));

            if (!__instance.components.Any(c => c is Context.HistoryGameComponent))
                __instance.components.Add(new Context.HistoryGameComponent(__instance));
        }
    }
}
