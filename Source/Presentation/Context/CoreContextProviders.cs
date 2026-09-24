using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Context;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Interfaces.Npc;
using RimMind.Application.Common.Models.Context;
using RimMind.Domain.ValueObjects;
using Verse;

namespace RimMind.Presentation.Context
{
    /// <summary>
    /// Migrated Core context providers using the new ContextProviderDef async API.
    /// Replaces the obsolete ContextKeyRegistry.RegisterCoreKeys() static registrations.
    /// </summary>
    public static class CoreContextProviders
    {
        private static Pawn? ResolvePawn(int pawnId)
        {
            if (pawnId <= 0) return null;
            return RimMind.Presentation.Api.RimMindPawnLookup.FindPawnByNumber(pawnId);
        }

        private static string? NullIfEmpty(string? value)
        {
            return string.IsNullOrEmpty(value) ? null : value;
        }

        public static void RegisterAll(IContextKeyRegistry registry,
            ITranslationService? translationService = null,
            IContextKeyProvider? contextKeyProvider = null,
            INpcManagerAccessor? npcManagers = null)
        {
            var ctx = contextKeyProvider;
            string? Translate(string key, params object[] args) =>
                translationService?.Translate(key, args);

            // ── L0_Static (stalenessTicks: 0 — always fresh, scenario-dependent) ──

            registry.Register(new ContextProviderDef(
                key: "system_instruction",
                layer: ContextLayer.L0_Static,
                priority: 1.0f,
                provider: async (pctx, ct) =>
                {
                    if (pctx.Scenario == ScenarioIds.Storyteller)
                        return "You are the RimWorld storyteller AI. Based on the colony's current situation, select the most appropriate incident event. " +
                            "Consider colony wealth, threat level, food supply, colonist count, and recent events. " +
                            "Output must be valid JSON matching the IncidentOutput schema.";
                    if (pctx.Scenario == ScenarioIds.Decision)
                        return null;
                    if (pctx.Scenario == ScenarioIds.Dialogue)
                        return null;
                    var pawn = ResolvePawn(pctx.PawnId);
                    if (pawn == null) return null;
                    var profile = npcManagers?.Current?.GetNpc($"NPC-{pawn.thingIDNumber}");
                    return NullIfEmpty(profile?.SystemPrompt);
                },
                ownerMod: "Core",
                stalenessTicks: 0,
                cacheScope: CacheScope.Static));

            registry.Register(new ContextProviderDef(
                key: "npc_identity",
                layer: ContextLayer.L0_Static,
                priority: 1.0f,
                provider: async (pctx, ct) =>
                {
                    if (pctx.Scenario == ScenarioIds.Decision)
                        return null;
                    if (pctx.Scenario == ScenarioIds.Dialogue)
                        return null;
                    var pawn = ResolvePawn(pctx.PawnId);
                    if (pawn == null) return null;
                    var profile = npcManagers?.Current?.GetNpc($"NPC-{pawn.thingIDNumber}");
                    if (profile == null) return null;
                    var sb = new StringBuilder();
                    sb.AppendLine(Translate("RimMind.Prompt.Identity.Name", profile.Name) ?? "");
                    if (!string.IsNullOrEmpty(profile.ShortName))
                        sb.AppendLine(Translate("RimMind.Prompt.Identity.ShortName", profile.ShortName) ?? "");
                    if (!string.IsNullOrEmpty(profile.CharacterDescription))
                        sb.AppendLine(Translate("RimMind.Prompt.Identity.Description", profile.CharacterDescription) ?? "");
                    return NullIfEmpty(sb.ToString().TrimEnd());
                },
                ownerMod: "Core",
                stalenessTicks: 0,
                cacheScope: CacheScope.Pawn));

            registry.Register(new ContextProviderDef(
                key: "npc_commands",
                layer: ContextLayer.L0_Static,
                priority: 1.0f,
                provider: async (pctx, ct) =>
                {
                    if (pctx.Scenario == ScenarioIds.Decision) return null;
                    if (pctx.Scenario == ScenarioIds.Dialogue) return null;
                    var pawn = ResolvePawn(pctx.PawnId);
                    if (pawn == null) return null;
                    var profile = npcManagers?.Current?.GetNpc($"NPC-{pawn.thingIDNumber}");
                    if (profile == null || profile.Commands.Count == 0) return null;
                    var sb = new StringBuilder();
                    sb.AppendLine(Translate("RimMind.Prompt.Commands.Available") ?? "");
                    foreach (var cmd in profile.Commands)
                        sb.AppendLine(Translate("RimMind.Prompt.Commands.Entry", cmd.Name, cmd.Description) ?? "");
                    return NullIfEmpty(sb.ToString().TrimEnd());
                },
                ownerMod: "Core",
                stalenessTicks: 0,
                cacheScope: CacheScope.Pawn));

            registry.Register(new ContextProviderDef(
                key: "world_rules",
                layer: ContextLayer.L0_Static,
                priority: 1.0f,
                provider: async (pctx, ct) =>
                {
                    if (pctx.Scenario == ScenarioIds.Storyteller)
                    {
                        var sb = new StringBuilder();
                        sb.AppendLine("Storyteller rules:");
                        sb.AppendLine("- Only select from available RimWorld incident definitions");
                        sb.AppendLine("- Consider threat level relative to colony wealth");
                        sb.AppendLine("- Balance positive and negative events");
                        sb.AppendLine("- Food shortages should trigger related events");
                        sb.AppendLine("- Low mood colonists may need positive events");
                        return sb.ToString().TrimEnd();
                    }
                    var sb2 = new StringBuilder();
                    sb2.AppendLine(Translate("RimMind.Prompt.WorldRules.Header") ?? "");
                    sb2.AppendLine(Translate("RimMind.Prompt.WorldRules.Survival") ?? "");
                    sb2.AppendLine(Translate("RimMind.Prompt.WorldRules.Combat") ?? "");
                    sb2.AppendLine(Translate("RimMind.Prompt.WorldRules.Relationships") ?? "");
                    sb2.AppendLine(Translate("RimMind.Prompt.WorldRules.Weather") ?? "");
                    sb2.AppendLine(Translate("RimMind.Prompt.WorldRules.Medical") ?? "");
                    return NullIfEmpty(sb2.ToString().TrimEnd());
                },
                ownerMod: "Core",
                stalenessTicks: 0,
                cacheScope: CacheScope.Static));

            registry.Register(new ContextProviderDef(
                key: "npc_task_instruction",
                layer: ContextLayer.L0_Static,
                priority: 1.0f,
                provider: async (pctx, ct) =>
                {
                    if (pctx.Scenario == ScenarioIds.Storyteller)
                        return "Select the most fitting incident for the colony's current state. Return structured JSON with defName, reason, and optional params.";
                    if (pctx.Scenario == ScenarioIds.Decision)
                        return NullIfEmpty(Translate("RimMind.Prompt.TaskInstruction.WorldOnly"));
                    if (pctx.Scenario == ScenarioIds.Dialogue)
                        return NullIfEmpty(Translate("RimMind.Prompt.TaskInstruction.WorldOnly"));
                    return NullIfEmpty(Translate("RimMind.Prompt.TaskInstruction.Base"));
                },
                ownerMod: "Core",
                stalenessTicks: 0,
                cacheScope: CacheScope.Static));

            // ── L1_Baseline (stalenessTicks: 3000 ~50s) ──

            if (ctx == null) return;

            registry.Register(new ContextProviderDef(
                key: "map_structure",
                layer: ContextLayer.L1_Baseline,
                priority: 0.95f,
                provider: async (pctx, ct) =>
                {
                    var pawn = ResolvePawn(pctx.PawnId);
                    if (pawn?.Map == null) return null;
                    var entries = ctx.BuildMapContextEntries(pawn.Map);
                    if (entries == null || entries.Count == 0) return null;
                    return string.Join("\n", entries.Select(e => e.Content));
                },
                ownerMod: "Core",
                stalenessTicks: 3000,
                cacheScope: CacheScope.Map));

            registry.Register(new ContextProviderDef(
                key: "pawn_base_info",
                layer: ContextLayer.L1_Baseline,
                priority: 0.95f,
                provider: async (pctx, ct) =>
                {
                    var pawn = ResolvePawn(pctx.PawnId);
                    if (pawn == null) return null;
                    return NullIfEmpty(ctx.ExtractPawnBaseInfo(pawn));
                },
                ownerMod: "Core",
                stalenessTicks: 3000,
                cacheScope: CacheScope.Pawn));

            registry.Register(new ContextProviderDef(
                key: "fixed_relations",
                layer: ContextLayer.L1_Baseline,
                priority: 0.9f,
                provider: async (pctx, ct) =>
                {
                    var pawn = ResolvePawn(pctx.PawnId);
                    if (pawn == null) return null;
                    return NullIfEmpty(ctx.ExtractFixedRelations(pawn));
                },
                ownerMod: "Core",
                stalenessTicks: 3000,
                cacheScope: CacheScope.Pawn));

            registry.Register(new ContextProviderDef(
                key: "ideology",
                layer: ContextLayer.L1_Baseline,
                priority: 0.9f,
                provider: async (pctx, ct) =>
                {
                    var pawn = ResolvePawn(pctx.PawnId);
                    if (pawn == null) return null;
                    return NullIfEmpty(ctx.ExtractIdeology(pawn));
                },
                ownerMod: "Core",
                stalenessTicks: 3000,
                cacheScope: CacheScope.Pawn));

            registry.Register(new ContextProviderDef(
                key: "skills_summary",
                layer: ContextLayer.L1_Baseline,
                priority: 0.85f,
                provider: async (pctx, ct) =>
                {
                    var pawn = ResolvePawn(pctx.PawnId);
                    if (pawn == null) return null;
                    return NullIfEmpty(ctx.ExtractSkillsSummary(pawn));
                },
                ownerMod: "Core",
                stalenessTicks: 3000,
                cacheScope: CacheScope.Pawn));

            // ── L2_Environment (stalenessTicks: 1500 ~25s) ──

            registry.Register(new ContextProviderDef(
                key: "current_area",
                layer: ContextLayer.L2_Environment,
                priority: 0.7f,
                provider: async (pctx, ct) =>
                {
                    var pawn = ResolvePawn(pctx.PawnId);
                    if (pawn == null) return null;
                    return NullIfEmpty(ctx.ExtractCurrentArea(pawn));
                },
                ownerMod: "Core",
                stalenessTicks: 1500,
                cacheScope: CacheScope.Pawn));

            registry.Register(new ContextProviderDef(
                key: "weather",
                layer: ContextLayer.L2_Environment,
                priority: 0.6f,
                provider: async (pctx, ct) =>
                {
                    var pawn = ResolvePawn(pctx.PawnId);
                    if (pawn == null) return null;
                    return NullIfEmpty(ctx.ExtractWeather(pawn));
                },
                ownerMod: "Core",
                stalenessTicks: 1500,
                cacheScope: CacheScope.Map));

            registry.Register(new ContextProviderDef(
                key: "time_of_day",
                layer: ContextLayer.L2_Environment,
                priority: 0.65f,
                provider: async (pctx, ct) =>
                {
                    var pawn = ResolvePawn(pctx.PawnId);
                    if (pawn == null) return null;
                    return NullIfEmpty(ctx.ExtractTimeOfDay(pawn));
                },
                ownerMod: "Core",
                stalenessTicks: 1500,
                cacheScope: CacheScope.Map));

            registry.Register(new ContextProviderDef(
                key: "nearby_pawns",
                layer: ContextLayer.L2_Environment,
                priority: 0.7f,
                provider: async (pctx, ct) =>
                {
                    var pawn = ResolvePawn(pctx.PawnId);
                    if (pawn == null) return null;
                    return NullIfEmpty(ctx.ExtractNearbyPawns(pawn));
                },
                ownerMod: "Core",
                stalenessTicks: 1500,
                cacheScope: CacheScope.Pawn));

            registry.Register(new ContextProviderDef(
                key: "season",
                layer: ContextLayer.L2_Environment,
                priority: 0.5f,
                provider: async (pctx, ct) =>
                {
                    var pawn = ResolvePawn(pctx.PawnId);
                    if (pawn == null) return null;
                    return NullIfEmpty(ctx.ExtractSeason(pawn));
                },
                ownerMod: "Core",
                stalenessTicks: 1500,
                cacheScope: CacheScope.Map));

            registry.Register(new ContextProviderDef(
                key: "colony_status",
                layer: ContextLayer.L2_Environment,
                priority: 0.6f,
                provider: async (pctx, ct) =>
                {
                    var pawn = ResolvePawn(pctx.PawnId);
                    if (pawn == null) return null;
                    return NullIfEmpty(ctx.ExtractColonyStatus(pawn));
                },
                ownerMod: "Core",
                stalenessTicks: 1500,
                cacheScope: CacheScope.Map));

            // ── L3_State (stalenessTicks: 750 ~12.5s) ──

            registry.Register(new ContextProviderDef(
                key: "health",
                layer: ContextLayer.L3_State,
                priority: 0.3f,
                provider: async (pctx, ct) =>
                {
                    var pawn = ResolvePawn(pctx.PawnId);
                    if (pawn == null) return null;
                    return NullIfEmpty(ctx.ExtractHealth(pawn));
                },
                ownerMod: "Core",
                stalenessTicks: 750,
                cacheScope: CacheScope.Pawn));

            registry.Register(new ContextProviderDef(
                key: "mood",
                layer: ContextLayer.L3_State,
                priority: 0.3f,
                provider: async (pctx, ct) =>
                {
                    var pawn = ResolvePawn(pctx.PawnId);
                    if (pawn == null) return null;
                    return NullIfEmpty(ctx.ExtractMood(pawn));
                },
                ownerMod: "Core",
                stalenessTicks: 750,
                cacheScope: CacheScope.Pawn));

            registry.Register(new ContextProviderDef(
                key: "current_job",
                layer: ContextLayer.L3_State,
                priority: 0.25f,
                provider: async (pctx, ct) =>
                {
                    var pawn = ResolvePawn(pctx.PawnId);
                    if (pawn == null) return null;
                    return NullIfEmpty(ctx.ExtractCurrentJob(pawn));
                },
                ownerMod: "Core",
                stalenessTicks: 750,
                cacheScope: CacheScope.Pawn));

            registry.Register(new ContextProviderDef(
                key: "combat_status",
                layer: ContextLayer.L3_State,
                priority: 0.2f,
                provider: async (pctx, ct) =>
                {
                    var pawn = ResolvePawn(pctx.PawnId);
                    if (pawn == null) return null;
                    return NullIfEmpty(ctx.ExtractCombatStatus(pawn));
                },
                ownerMod: "Core",
                stalenessTicks: 750,
                cacheScope: CacheScope.Pawn));

            registry.Register(new ContextProviderDef(
                key: "target_info",
                layer: ContextLayer.L3_State,
                priority: 0.15f,
                provider: async (pctx, ct) =>
                {
                    var pawn = ResolvePawn(pctx.PawnId);
                    if (pawn == null) return null;
                    return NullIfEmpty(ctx.ExtractTargetInfo(pawn));
                },
                ownerMod: "Core",
                stalenessTicks: 750,
                cacheScope: CacheScope.Pawn));

            registry.Register(new ContextProviderDef(
                key: "task_progress",
                layer: ContextLayer.L3_State,
                priority: 0.2f,
                provider: async (pctx, ct) =>
                {
                    var pawn = ResolvePawn(pctx.PawnId);
                    if (pawn == null) return null;
                    return NullIfEmpty(ctx.ExtractTaskProgress(pawn));
                },
                ownerMod: "Core",
                stalenessTicks: 750,
                cacheScope: CacheScope.Pawn));
        }
    }
}
