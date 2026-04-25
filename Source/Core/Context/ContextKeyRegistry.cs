using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using RimMind.Core.Internal;
using RimMind.Core.Npc;

namespace RimMind.Core.Context
{
    public static class ContextKeyRegistry
    {
        private static readonly Dictionary<string, KeyMeta> _keys = new Dictionary<string, KeyMeta>();
        private static bool _coreRegistered = false;
        private static string _currentScenario = string.Empty;
        public static string CurrentScenario { get => _currentScenario; set => _currentScenario = value; }

        public static void Register(string key, ContextLayer layer, float priority,
            Func<Pawn, List<ContextEntry>> provider, string ownerMod,
            bool isIndexable = false, float[]? keyEmbedding = null)
        {
            if (_keys.ContainsKey(key))
            {
                var old = _keys[key];
                Log.Warning($"[RimMind] ContextKey '{key}' registered by '{old.OwnerMod}' " +
                    $"overwritten by '{ownerMod}'.");
            }
            _keys[key] = new KeyMeta(key, layer, priority, provider, ownerMod,
                isIndexable, keyEmbedding);
        }

        public static bool Unregister(string key)
        {
            return _keys.Remove(key);
        }

        public static List<KeyMeta> GetAll()
        {
            return new List<KeyMeta>(_keys.Values);
        }

        public static List<KeyMeta> GetByLayer(ContextLayer layer)
        {
            var result = new List<KeyMeta>();
            foreach (var kvp in _keys)
            {
                if (kvp.Value.Layer == layer)
                    result.Add(kvp.Value);
            }
            return result;
        }

        public static KeyMeta? GetKey(string key)
        {
            return _keys.TryGetValue(key, out var meta) ? meta : null;
        }

        private static List<ContextEntry> WrapEntry(string value)
        {
            return string.IsNullOrEmpty(value) ? new List<ContextEntry>() : new List<ContextEntry> { new ContextEntry(value) };
        }

        public static void RegisterCoreKeys()
        {
            if (_coreRegistered) return;
            _coreRegistered = true;

            Register("system_instruction", ContextLayer.L0_Static, 1.0f,
                pawn =>
                {
                    var profile = NpcManager.Instance?.GetNpc($"NPC-{pawn.thingIDNumber}");
                    return WrapEntry(profile?.SystemPrompt ?? "");
                }, "Core");
            Register("npc_identity", ContextLayer.L0_Static, 1.0f,
                pawn =>
                {
                    var profile = NpcManager.Instance?.GetNpc($"NPC-{pawn.thingIDNumber}");
                    if (profile == null) return WrapEntry("");
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine("RimMind.Core.Prompt.Identity.Name".Translate(profile.Name));
                    if (!string.IsNullOrEmpty(profile.ShortName))
                        sb.AppendLine("RimMind.Core.Prompt.Identity.ShortName".Translate(profile.ShortName));
                    if (!string.IsNullOrEmpty(profile.CharacterDescription))
                        sb.AppendLine("RimMind.Core.Prompt.Identity.Description".Translate(profile.CharacterDescription));
                    return WrapEntry(sb.ToString().TrimEnd());
                }, "Core");
            Register("npc_commands", ContextLayer.L0_Static, 1.0f,
                pawn =>
                {
                    var profile = NpcManager.Instance?.GetNpc($"NPC-{pawn.thingIDNumber}");
                    if (profile == null || profile.Commands.Count == 0) return WrapEntry("");
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine("RimMind.Core.Prompt.Commands.Available".Translate());
                    foreach (var cmd in profile.Commands)
                        sb.AppendLine("RimMind.Core.Prompt.Commands.Entry".Translate(cmd.Name, cmd.Description));
                    return WrapEntry(sb.ToString().TrimEnd());
                }, "Core");
            Register("world_rules", ContextLayer.L0_Static, 1.0f,
                pawn =>
                {
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine("RimMind.Core.Prompt.WorldRules.Header".Translate());
                    sb.AppendLine("RimMind.Core.Prompt.WorldRules.Survival".Translate());
                    sb.AppendLine("RimMind.Core.Prompt.WorldRules.Combat".Translate());
                    sb.AppendLine("RimMind.Core.Prompt.WorldRules.Relationships".Translate());
                    sb.AppendLine("RimMind.Core.Prompt.WorldRules.Weather".Translate());
                    sb.AppendLine("RimMind.Core.Prompt.WorldRules.Medical".Translate());
                    return WrapEntry(sb.ToString().TrimEnd());
                }, "Core");
            Register("npc_task_instruction", ContextLayer.L0_Static, 1.0f,
                pawn =>
                {
                    return WrapEntry("RimMind.Core.Prompt.TaskInstruction.Base".Translate());
                }, "Core");

            Register("map_structure", ContextLayer.L1_Baseline, 0.95f,
                pawn => pawn.Map != null ? WrapEntry(GameContextBuilder.BuildMapContext(pawn.Map)) : WrapEntry(""), "Core");
            Register("pawn_base_info", ContextLayer.L1_Baseline, 0.95f,
                pawn => WrapEntry(GameContextBuilder.ExtractPawnBaseInfo(pawn)), "Core");
            Register("fixed_relations", ContextLayer.L1_Baseline, 0.9f,
                pawn => WrapEntry(GameContextBuilder.ExtractFixedRelations(pawn)), "Core");
            Register("ideology", ContextLayer.L1_Baseline, 0.9f,
                pawn => WrapEntry(GameContextBuilder.ExtractIdeology(pawn)), "Core");
            Register("skills_summary", ContextLayer.L1_Baseline, 0.85f,
                pawn => WrapEntry(GameContextBuilder.ExtractSkillsSummary(pawn)), "Core");

            Register("current_area", ContextLayer.L2_Environment, 0.7f,
                pawn => WrapEntry(GameContextBuilder.ExtractCurrentArea(pawn)), "Core");
            Register("weather", ContextLayer.L2_Environment, 0.6f,
                pawn => WrapEntry(GameContextBuilder.ExtractWeather(pawn)), "Core");
            Register("time_of_day", ContextLayer.L2_Environment, 0.65f,
                pawn => WrapEntry(GameContextBuilder.ExtractTimeOfDay(pawn)), "Core");
            Register("nearby_pawns", ContextLayer.L2_Environment, 0.7f,
                pawn => WrapEntry(GameContextBuilder.ExtractNearbyPawns(pawn)), "Core");
            Register("season", ContextLayer.L2_Environment, 0.5f,
                pawn => WrapEntry(GameContextBuilder.ExtractSeason(pawn)), "Core");
            Register("colony_status", ContextLayer.L2_Environment, 0.6f,
                pawn => WrapEntry(GameContextBuilder.ExtractColonyStatus(pawn)), "Core");

            Register("health", ContextLayer.L3_State, 0.3f,
                pawn => WrapEntry(GameContextBuilder.ExtractHealth(pawn)), "Core");
            Register("mood", ContextLayer.L3_State, 0.3f,
                pawn => WrapEntry(GameContextBuilder.ExtractMood(pawn)), "Core");
            Register("current_job", ContextLayer.L3_State, 0.25f,
                pawn => WrapEntry(GameContextBuilder.ExtractCurrentJob(pawn)), "Core");
            Register("combat_status", ContextLayer.L3_State, 0.2f,
                pawn => WrapEntry(GameContextBuilder.ExtractCombatStatus(pawn)), "Core");
            Register("target_info", ContextLayer.L3_State, 0.15f,
                pawn => WrapEntry(GameContextBuilder.ExtractTargetInfo(pawn)), "Core");
            Register("task_progress", ContextLayer.L3_State, 0.2f,
                pawn =>
                {
                    var comp = pawn.TryGetComp<RimMind.Core.Comps.CompPawnAgent>();
                    if (comp == null || comp.Agent == null) return WrapEntry("");
                    var goals = comp.Agent.GoalStack.ActiveGoals;
                    if (goals.Count == 0) return WrapEntry("");
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine("Current goals:");
                    foreach (var g in goals)
                        sb.AppendLine($"- [{g.Category}] {g.Description} (priority {g.Priority}, {g.Status})");
                    return WrapEntry(sb.ToString().TrimEnd());
                }, "Core");
        }

        public static void Clear()
        {
            _keys.Clear();
            _coreRegistered = false;
        }
    }
}
