using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Constants;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Context;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Interfaces.Npc;
using RimMind.Application.Common.Models.Context;
using RimMind.Domain.ValueObjects;
using RimMind.Presentation.Agent;
using RimMind.Presentation.Api;
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
            var pawn = RimMindPawnLookup.FindPawnByNumber(pawnId);
            return RimMindPawnLookup.IsActiveSpawnedColonist(pawn) ? pawn : null;
        }

        private static string? NullIfEmpty(string? value)
        {
            return string.IsNullOrEmpty(value) ? null : value;
        }

        private static Task<string?> ReadPawnContextAsync(int pawnId, Func<Pawn, string?> extractor, CancellationToken ct)
        {
            if (pawnId <= 0) return Task.FromResult<string?>(null);
            return RimMindAPI.MainThreadSnapshot.GetAsync<string?>(() =>
            {
                var pawn = ResolvePawn(pawnId);
                if (pawn == null) return null;
                return NullIfEmpty(extractor(pawn));
            }, null, ct: ct);
        }

        private static bool IsAdvisorOwnedScenario(string? scenario)
            => string.Equals(scenario, ScenarioIds.Decision, StringComparison.Ordinal)
                || string.Equals(scenario, ScenarioIds.AdvisorDecision, StringComparison.Ordinal);

        private static bool IsDialogueOwnedScenario(string? scenario)
            => string.Equals(scenario, ScenarioIds.Dialogue, StringComparison.Ordinal)
                || string.Equals(scenario, ScenarioIds.CrisisDialogue, StringComparison.Ordinal);

        public static void RegisterAll(IContextKeyRegistry registry,
            ITranslationService? translationService = null,
            IContextKeyProvider? contextKeyProvider = null,
            INpcManagerAccessor? npcManagers = null)
        {
            var ctx = contextKeyProvider;
            string? Translate(string key, params object[] args)
            {
                if (translationService == null) return null;
                if (args == null || args.Length == 0) return translationService.Translate(key);
                if (args.Length == 1) return translationService.Translate(key, args[0]);
                if (args.Length == 2) return translationService.Translate(key, args[0], args[1]);
                return translationService.Translate(key, args[0], args[1], args[2]);
            }

            // ── L0_Static (stalenessTicks: 0 — always fresh, scenario-dependent) ──

            registry.Register(new ContextProviderDef(
                key: "system_instruction",
                layer: ContextLayer.L0_Static,
                priority: 1.0f,
                provider: async (pctx, ct) =>
                {
                    // Submodule-owned scenarios supply their own system instructions.
                    if (IsAdvisorOwnedScenario(pctx.Scenario) || IsDialogueOwnedScenario(pctx.Scenario))
                        return null;
                    if (pctx.Scenario == ScenarioIds.Storyteller)
                        return null;
                    if (pctx.Scenario == ScenarioIds.AgentDecision)
                    {
                        string? pawnPrompt = null;
                        if (pctx.PawnId > 0)
                        {
                            pawnPrompt = await RimMindAPI.MainThreadSnapshot.GetAsync<string?>(() =>
                            {
                                var pawn = ResolvePawn(pctx.PawnId);
                                if (pawn == null) return null;
                                var profile = npcManagers?.Current?.GetNpc($"NPC-{pawn.thingIDNumber}");
                                return NullIfEmpty(profile?.SystemPrompt);
                            }, null, ct: ct).ConfigureAwait(false);
                        }

                        const string agentRole =
                            "You are the autonomous mind of this RimWorld colonist. " +
                            "Evaluate your immediate survival needs, colony priorities, and current environment, " +
                            "then choose the best concrete tool call to execute (or wait if no action is needed).";
                        return string.IsNullOrWhiteSpace(pawnPrompt) ? agentRole : $"{agentRole}\n{pawnPrompt}";
                    }
                    if (pctx.PawnId <= 0) return null;
                    return await RimMindAPI.MainThreadSnapshot.GetAsync<string?>(() =>
                    {
                        var pawn = ResolvePawn(pctx.PawnId);
                        if (pawn == null) return null;
                        var profile = npcManagers?.Current?.GetNpc($"NPC-{pawn.thingIDNumber}");
                        return NullIfEmpty(profile?.SystemPrompt);
                    }, null, ct: ct).ConfigureAwait(false);
                },
                ownerMod: RimMindOwnerConsts.CoreModId,
                stalenessTicks: 0,
                cacheScope: CacheScope.Static));

            registry.Register(new ContextProviderDef(
                key: "npc_identity",
                layer: ContextLayer.L0_Static,
                priority: 1.0f,
                provider: async (pctx, ct) =>
                {
                    if (IsAdvisorOwnedScenario(pctx.Scenario) || IsDialogueOwnedScenario(pctx.Scenario))
                        return null;
                    if (pctx.PawnId <= 0) return null;
                    return await RimMindAPI.MainThreadSnapshot.GetAsync<string?>(() =>
                    {
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
                    }, null, ct: ct).ConfigureAwait(false);
                },
                ownerMod: RimMindOwnerConsts.CoreModId,
                stalenessTicks: 0,
                cacheScope: CacheScope.Pawn));

            registry.Register(new ContextProviderDef(
                key: "npc_commands",
                layer: ContextLayer.L0_Static,
                priority: 1.0f,
                provider: async (pctx, ct) =>
                {
                    if (IsAdvisorOwnedScenario(pctx.Scenario) || IsDialogueOwnedScenario(pctx.Scenario))
                        return null;
                    if (pctx.PawnId <= 0) return null;
                    return await RimMindAPI.MainThreadSnapshot.GetAsync<string?>(() =>
                    {
                        var pawn = ResolvePawn(pctx.PawnId);
                        if (pawn == null) return null;
                        var profile = npcManagers?.Current?.GetNpc($"NPC-{pawn.thingIDNumber}");
                        if (profile == null || profile.Commands.Count == 0) return null;
                        var sb = new StringBuilder();
                        sb.AppendLine(Translate("RimMind.Prompt.Commands.Available") ?? "");
                        foreach (var cmd in profile.Commands)
                            sb.AppendLine(Translate("RimMind.Prompt.Commands.Entry", cmd.Name, cmd.Description) ?? "");
                        return NullIfEmpty(sb.ToString().TrimEnd());
                    }, null, ct: ct).ConfigureAwait(false);
                },
                ownerMod: RimMindOwnerConsts.CoreModId,
                stalenessTicks: 0,
                cacheScope: CacheScope.Pawn));

            registry.Register(new ContextProviderDef(
                key: "world_rules",
                layer: ContextLayer.L0_Static,
                priority: 1.0f,
                provider: async (pctx, ct) =>
                {
                    if (pctx.Scenario == ScenarioIds.Storyteller)
                        return null;
                    return await RimMindAPI.MainThreadSnapshot.GetAsync<string?>(() =>
                    {
                        var sb2 = new StringBuilder();
                        sb2.AppendLine(Translate("RimMind.Prompt.WorldRules.Header") ?? "");
                        sb2.AppendLine(Translate("RimMind.Prompt.WorldRules.Survival") ?? "");
                        sb2.AppendLine(Translate("RimMind.Prompt.WorldRules.Combat") ?? "");
                        sb2.AppendLine(Translate("RimMind.Prompt.WorldRules.Relationships") ?? "");
                        sb2.AppendLine(Translate("RimMind.Prompt.WorldRules.Weather") ?? "");
                        sb2.AppendLine(Translate("RimMind.Prompt.WorldRules.Medical") ?? "");
                        return NullIfEmpty(sb2.ToString().TrimEnd());
                    }, null, ct: ct).ConfigureAwait(false);
                },
                ownerMod: RimMindOwnerConsts.CoreModId,
                stalenessTicks: 0,
                cacheScope: CacheScope.Static));

            registry.Register(new ContextProviderDef(
                key: "npc_task_instruction",
                layer: ContextLayer.L0_Static,
                priority: 1.0f,
                provider: async (pctx, ct) =>
                {
                    if (pctx.Scenario == ScenarioIds.Storyteller)
                        return null;
                    return await RimMindAPI.MainThreadSnapshot.GetAsync<string?>(() =>
                    {
                        if (IsAdvisorOwnedScenario(pctx.Scenario) || IsDialogueOwnedScenario(pctx.Scenario))
                            return NullIfEmpty(Translate("RimMind.Prompt.TaskInstruction.WorldOnly"));
                        return NullIfEmpty(Translate("RimMind.Prompt.TaskInstruction.Base"));
                    }, null, ct: ct).ConfigureAwait(false);
                },
                ownerMod: RimMindOwnerConsts.CoreModId,
                stalenessTicks: 0,
                cacheScope: CacheScope.Static));

            // ── L1_Baseline (stalenessTicks: 3000 ~50s — strictly KV-Cache prefix-stable) ──

            if (ctx == null) return;

            registry.Register(new ContextProviderDef(
                key: "map_structure",
                layer: ContextLayer.L1_Baseline,
                priority: 0.95f,
                provider: (pctx, ct) => ReadPawnContextAsync(pctx.PawnId, pawn =>
                {
                    if (pawn.Map == null) return null;
                    if (ctx is GameContextBuilder gcb)
                    {
                        return NullIfEmpty(gcb.BuildStaticMapTopology(pawn.Map));
                    }
                    var entries = ctx.BuildMapContextEntries(pawn.Map);
                    if (entries == null || entries.Count == 0) return null;
                    return string.Join("\n", entries.Select(e => e.Content));
                }, ct),
                ownerMod: RimMindOwnerConsts.CoreModId,
                stalenessTicks: 3000,
                cacheScope: CacheScope.Map));

            registry.Register(new ContextProviderDef(
                key: "pawn_base_info",
                layer: ContextLayer.L1_Baseline,
                priority: 0.95f,
                provider: (pctx, ct) => ReadPawnContextAsync(pctx.PawnId, ctx.ExtractPawnBaseInfo, ct),
                ownerMod: RimMindOwnerConsts.CoreModId,
                stalenessTicks: 3000,
                cacheScope: CacheScope.Pawn));

            registry.Register(new ContextProviderDef(
                key: "fixed_relations",
                layer: ContextLayer.L1_Baseline,
                priority: 0.9f,
                provider: (pctx, ct) => ReadPawnContextAsync(pctx.PawnId, ctx.ExtractFixedRelations, ct),
                ownerMod: RimMindOwnerConsts.CoreModId,
                stalenessTicks: 3000,
                cacheScope: CacheScope.Pawn));

            registry.Register(new ContextProviderDef(
                key: "ideology",
                layer: ContextLayer.L1_Baseline,
                priority: 0.9f,
                provider: (pctx, ct) => ReadPawnContextAsync(pctx.PawnId, ctx.ExtractIdeology, ct),
                ownerMod: RimMindOwnerConsts.CoreModId,
                stalenessTicks: 3000,
                cacheScope: CacheScope.Pawn));

            registry.Register(new ContextProviderDef(
                key: "skills_summary",
                layer: ContextLayer.L1_Baseline,
                priority: 0.85f,
                provider: (pctx, ct) => ReadPawnContextAsync(pctx.PawnId, ctx.ExtractSkillsSummary, ct),
                ownerMod: RimMindOwnerConsts.CoreModId,
                stalenessTicks: 3000,
                cacheScope: CacheScope.Pawn));

            // ── L2_Environment (stalenessTicks: 1500 ~25s — volatile map/environment context) ──

            registry.Register(new ContextProviderDef(
                key: "colony_resources",
                layer: ContextLayer.L2_Environment,
                priority: 0.72f,
                provider: (pctx, ct) => ReadPawnContextAsync(pctx.PawnId, pawn =>
                {
                    if (pawn.Map == null) return null;
                    if (ctx is GameContextBuilder gcb)
                    {
                        return NullIfEmpty(gcb.BuildVolatileColonyResources(pawn.Map));
                    }
                    return null;
                }, ct),
                ownerMod: RimMindOwnerConsts.CoreModId,
                stalenessTicks: 1500,
                cacheScope: CacheScope.Map));

            registry.Register(new ContextProviderDef(
                key: "current_area",
                layer: ContextLayer.L2_Environment,
                priority: 0.7f,
                provider: (pctx, ct) => ReadPawnContextAsync(pctx.PawnId, ctx.ExtractCurrentArea, ct),
                ownerMod: RimMindOwnerConsts.CoreModId,
                stalenessTicks: 1500,
                cacheScope: CacheScope.Pawn));

            registry.Register(new ContextProviderDef(
                key: "weather",
                layer: ContextLayer.L2_Environment,
                priority: 0.6f,
                provider: (pctx, ct) => ReadPawnContextAsync(pctx.PawnId, ctx.ExtractWeather, ct),
                ownerMod: RimMindOwnerConsts.CoreModId,
                stalenessTicks: 1500,
                cacheScope: CacheScope.Map));

            registry.Register(new ContextProviderDef(
                key: "time_of_day",
                layer: ContextLayer.L2_Environment,
                priority: 0.65f,
                provider: (pctx, ct) => ReadPawnContextAsync(pctx.PawnId, ctx.ExtractTimeOfDay, ct),
                ownerMod: RimMindOwnerConsts.CoreModId,
                stalenessTicks: 1500,
                cacheScope: CacheScope.Map));

            registry.Register(new ContextProviderDef(
                key: "nearby_pawns",
                layer: ContextLayer.L2_Environment,
                priority: 0.7f,
                provider: (pctx, ct) => ReadPawnContextAsync(pctx.PawnId, ctx.ExtractNearbyPawns, ct),
                ownerMod: RimMindOwnerConsts.CoreModId,
                stalenessTicks: 1500,
                cacheScope: CacheScope.Pawn));

            registry.Register(new ContextProviderDef(
                key: "season",
                layer: ContextLayer.L2_Environment,
                priority: 0.5f,
                provider: (pctx, ct) => ReadPawnContextAsync(pctx.PawnId, ctx.ExtractSeason, ct),
                ownerMod: RimMindOwnerConsts.CoreModId,
                stalenessTicks: 1500,
                cacheScope: CacheScope.Map));

            registry.Register(new ContextProviderDef(
                key: "colony_status",
                layer: ContextLayer.L2_Environment,
                priority: 0.6f,
                provider: (pctx, ct) => ReadPawnContextAsync(pctx.PawnId, ctx.ExtractColonyStatus, ct),
                ownerMod: RimMindOwnerConsts.CoreModId,
                stalenessTicks: 1500,
                cacheScope: CacheScope.Map));

            // ── L3_State (stalenessTicks: 750 ~12.5s) ──

            registry.Register(new ContextProviderDef(
                key: "health",
                layer: ContextLayer.L3_State,
                priority: 0.3f,
                provider: (pctx, ct) => ReadPawnContextAsync(pctx.PawnId, ctx.ExtractHealth, ct),
                ownerMod: RimMindOwnerConsts.CoreModId,
                stalenessTicks: 750,
                cacheScope: CacheScope.Pawn));

            registry.Register(new ContextProviderDef(
                key: "mood",
                layer: ContextLayer.L3_State,
                priority: 0.3f,
                provider: (pctx, ct) => ReadPawnContextAsync(pctx.PawnId, ctx.ExtractMood, ct),
                ownerMod: RimMindOwnerConsts.CoreModId,
                stalenessTicks: 750,
                cacheScope: CacheScope.Pawn));

            registry.Register(new ContextProviderDef(
                key: "mood_thoughts",
                layer: ContextLayer.L3_State,
                priority: 0.28f,
                provider: (pctx, ct) => ReadPawnContextAsync(pctx.PawnId, ctx.ExtractMoodThoughts, ct),
                ownerMod: RimMindOwnerConsts.CoreModId,
                stalenessTicks: 750,
                cacheScope: CacheScope.Pawn));

            registry.Register(new ContextProviderDef(
                key: "current_job",
                layer: ContextLayer.L3_State,
                priority: 0.25f,
                provider: (pctx, ct) => ReadPawnContextAsync(pctx.PawnId, ctx.ExtractCurrentJob, ct),
                ownerMod: RimMindOwnerConsts.CoreModId,
                stalenessTicks: 750,
                cacheScope: CacheScope.Pawn));

            registry.Register(new ContextProviderDef(
                key: "combat_status",
                layer: ContextLayer.L3_State,
                priority: 0.2f,
                provider: (pctx, ct) => ReadPawnContextAsync(pctx.PawnId, ctx.ExtractCombatStatus, ct),
                ownerMod: RimMindOwnerConsts.CoreModId,
                stalenessTicks: 750,
                cacheScope: CacheScope.Pawn));

            registry.Register(new ContextProviderDef(
                key: "target_info",
                layer: ContextLayer.L3_State,
                priority: 0.15f,
                provider: (pctx, ct) => ReadPawnContextAsync(pctx.PawnId, ctx.ExtractTargetInfo, ct),
                ownerMod: RimMindOwnerConsts.CoreModId,
                stalenessTicks: 750,
                cacheScope: CacheScope.Pawn));

            registry.Register(new ContextProviderDef(
                key: "task_progress",
                layer: ContextLayer.L3_State,
                priority: 0.2f,
                provider: (pctx, ct) => ReadPawnContextAsync(pctx.PawnId, ctx.ExtractTaskProgress, ct),
                ownerMod: RimMindOwnerConsts.CoreModId,
                stalenessTicks: 750,
                cacheScope: CacheScope.Pawn));
        }
    }
}
