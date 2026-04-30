using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using RimWorld;
using Verse;
using RimMind.Core.Internal;
using RimMind.Core.Npc;

namespace RimMind.Core.Context
{
    public static class ContextKeyRegistry
    {
        private static readonly ConcurrentDictionary<string, KeyMeta> _keys = new ConcurrentDictionary<string, KeyMeta>();
        private static bool _coreRegistered = false;
        private static string _currentScenario = string.Empty;
        public static string CurrentScenario { get => _currentScenario; set => _currentScenario = value; }

        private static string? _currentSpeakerName;
        public static string? CurrentSpeakerName { get => _currentSpeakerName; set => _currentSpeakerName = value; }

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
            return _keys.TryRemove(key, out _);
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
                    if (CurrentScenario == ScenarioIds.Storyteller)
                        return WrapEntry("You are the RimWorld storyteller AI. Based on the colony's current situation, select the most appropriate incident event. " +
                            "Consider colony wealth, threat level, food supply, colonist count, and recent events. " +
                            "Output must be valid JSON matching the IncidentOutput schema.");
                    if (CurrentScenario == ScenarioIds.Decision)
                        return WrapEntry("");
                    if (CurrentScenario == ScenarioIds.Dialogue)
                        return WrapEntry("");
                    if (pawn == null) return WrapEntry("");
                    var profile = NpcManager.Instance?.GetNpc($"NPC-{pawn.thingIDNumber}");
                    return WrapEntry(profile?.SystemPrompt ?? "");
                }, "Core");
            Register("npc_identity", ContextLayer.L0_Static, 1.0f,
                pawn =>
                {
                    if (CurrentScenario == ScenarioIds.Decision)
                        return WrapEntry("");
                    if (CurrentScenario == ScenarioIds.Dialogue)
                        return WrapEntry("");
                    if (pawn == null) return WrapEntry("");
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
                    if (CurrentScenario == ScenarioIds.Decision) return WrapEntry("");
                    if (CurrentScenario == ScenarioIds.Dialogue) return WrapEntry("");
                    if (pawn == null) return WrapEntry("");
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
                    if (CurrentScenario == ScenarioIds.Storyteller)
                    {
                        var sb = new System.Text.StringBuilder();
                        sb.AppendLine("Storyteller rules:");
                        sb.AppendLine("- Only select from available RimWorld incident definitions");
                        sb.AppendLine("- Consider threat level relative to colony wealth");
                        sb.AppendLine("- Balance positive and negative events");
                        sb.AppendLine("- Food shortages should trigger related events");
                        sb.AppendLine("- Low mood colonists may need positive events");
                        return WrapEntry(sb.ToString().TrimEnd());
                    }
                    var sb2 = new System.Text.StringBuilder();
                    sb2.AppendLine("RimMind.Core.Prompt.WorldRules.Header".Translate());
                    sb2.AppendLine("RimMind.Core.Prompt.WorldRules.Survival".Translate());
                    sb2.AppendLine("RimMind.Core.Prompt.WorldRules.Combat".Translate());
                    sb2.AppendLine("RimMind.Core.Prompt.WorldRules.Relationships".Translate());
                    sb2.AppendLine("RimMind.Core.Prompt.WorldRules.Weather".Translate());
                    sb2.AppendLine("RimMind.Core.Prompt.WorldRules.Medical".Translate());
                    return WrapEntry(sb2.ToString().TrimEnd());
                }, "Core");
            Register("npc_task_instruction", ContextLayer.L0_Static, 1.0f,
                pawn =>
                {
                    if (CurrentScenario == ScenarioIds.Storyteller)
                        return WrapEntry("Select the most fitting incident for the colony's current state. Return structured JSON with defName, reason, and optional params.");
                    if (CurrentScenario == ScenarioIds.Decision)
                        return WrapEntry("RimMind.Core.Prompt.TaskInstruction.WorldOnly".Translate());
                    if (CurrentScenario == ScenarioIds.Dialogue)
                        return WrapEntry("RimMind.Core.Prompt.TaskInstruction.WorldOnly".Translate());
                    return WrapEntry("RimMind.Core.Prompt.TaskInstruction.Base".Translate());
                }, "Core");

            Register("map_structure", ContextLayer.L1_Baseline, 0.95f,
                pawn => pawn?.Map != null ? WrapEntry(GameContextBuilder.BuildMapContext(pawn.Map)) : WrapEntry(""), "Core");
            Register("pawn_base_info", ContextLayer.L1_Baseline, 0.95f,
                pawn => pawn != null ? WrapEntry(GameContextBuilder.ExtractPawnBaseInfo(pawn)) : WrapEntry(""), "Core");
            Register("fixed_relations", ContextLayer.L1_Baseline, 0.9f,
                pawn => pawn != null ? WrapEntry(GameContextBuilder.ExtractFixedRelations(pawn)) : WrapEntry(""), "Core");
            Register("ideology", ContextLayer.L1_Baseline, 0.9f,
                pawn => pawn != null ? WrapEntry(GameContextBuilder.ExtractIdeology(pawn)) : WrapEntry(""), "Core");
            Register("skills_summary", ContextLayer.L1_Baseline, 0.85f,
                pawn => pawn != null ? WrapEntry(GameContextBuilder.ExtractSkillsSummary(pawn)) : WrapEntry(""), "Core");

            Register("current_area", ContextLayer.L2_Environment, 0.7f,
                pawn => pawn != null ? WrapEntry(GameContextBuilder.ExtractCurrentArea(pawn)) : WrapEntry(""), "Core");
            Register("weather", ContextLayer.L2_Environment, 0.6f,
                pawn => pawn != null ? WrapEntry(GameContextBuilder.ExtractWeather(pawn)) : WrapEntry(""), "Core");
            Register("time_of_day", ContextLayer.L2_Environment, 0.65f,
                pawn => pawn != null ? WrapEntry(GameContextBuilder.ExtractTimeOfDay(pawn)) : WrapEntry(""), "Core");
            Register("nearby_pawns", ContextLayer.L2_Environment, 0.7f,
                pawn => pawn != null ? WrapEntry(GameContextBuilder.ExtractNearbyPawns(pawn)) : WrapEntry(""), "Core");
            Register("season", ContextLayer.L2_Environment, 0.5f,
                pawn => pawn != null ? WrapEntry(GameContextBuilder.ExtractSeason(pawn)) : WrapEntry(""), "Core");
            Register("colony_status", ContextLayer.L2_Environment, 0.6f,
                pawn => pawn != null ? WrapEntry(GameContextBuilder.ExtractColonyStatus(pawn)) : WrapEntry(""), "Core");

            Register("health", ContextLayer.L3_State, 0.3f,
                pawn => pawn != null ? WrapEntry(GameContextBuilder.ExtractHealth(pawn)) : WrapEntry(""), "Core");
            Register("mood", ContextLayer.L3_State, 0.3f,
                pawn => pawn != null ? WrapEntry(GameContextBuilder.ExtractMood(pawn)) : WrapEntry(""), "Core");
            Register("current_job", ContextLayer.L3_State, 0.25f,
                pawn => pawn != null ? WrapEntry(GameContextBuilder.ExtractCurrentJob(pawn)) : WrapEntry(""), "Core");
            Register("combat_status", ContextLayer.L3_State, 0.2f,
                pawn => pawn != null ? WrapEntry(GameContextBuilder.ExtractCombatStatus(pawn)) : WrapEntry(""), "Core");
            Register("target_info", ContextLayer.L3_State, 0.15f,
                pawn => pawn != null ? WrapEntry(GameContextBuilder.ExtractTargetInfo(pawn)) : WrapEntry(""), "Core");
            Register("task_progress", ContextLayer.L3_State, 0.2f,
                pawn =>
                {
                    if (pawn == null) return WrapEntry("");
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
