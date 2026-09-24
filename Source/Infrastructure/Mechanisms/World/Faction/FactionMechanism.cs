using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using RimMind.Application.Common.Interfaces.Mechanisms;
using RimMind.Application.Common.Models.Mechanisms;
using RimMind.Domain.Enums;
using RimMind.Domain.ValueObjects;
using Verse;
using RimWorld;

namespace RimMind.Infrastructure.Mechanisms.World.Faction
{
    public sealed class FactionMechanism : GameMechanismBaseNoDef
    {
        public override string MechanismId => "world.faction";
        public override MechanismScope Scope => MechanismScope.World;
        public override MechanismRisk Risk => MechanismRisk.Dangerous;
        public override IReadOnlyList<MechanismOperationType> SupportedOperations => _supportedOps;
        public override MechanismDocs Docs => _docs;

        private static readonly IReadOnlyList<MechanismOperationType> _supportedOps =
            new List<MechanismOperationType> { MechanismOperationType.Query, MechanismOperationType.Set, MechanismOperationType.List }.AsReadOnly();

        private static readonly MechanismDocs _docs = new MechanismDocs
        {
            Summary = "Query and modify faction relations",
            QueryDescription = "Query faction information and relations",
            SetDescription = "Modify faction goodwill. DANGEROUS - affects faction relations permanently.",
            ListDescription = "List all factions in the world"
        };

        public override MechanismRisk GetRiskForOperation(MechanismOperationType operation)
        {
            return operation switch
            {
                MechanismOperationType.Set => MechanismRisk.Dangerous,
                MechanismOperationType.Query => MechanismRisk.Safe,
                MechanismOperationType.List => MechanismRisk.Safe,
                _ => Risk
            };
        }

        public override Task<Result<string, RimMindError>> ExecuteQueryAsync(MechanismReadArgs args, CancellationToken ct)
        {
            var playerFaction = global::RimWorld.Faction.OfPlayer;
            var factions = Find.FactionManager?.AllFactions?
                .Where(f => f?.def != null && !f.def.hidden && !f.IsPlayer)
                .Select(f => new
                {
                    def = f.def.defName,
                    name = f.Name ?? f.def.label ?? f.def.defName,
                    factionId = f.loadID,
                    goodwill = (playerFaction != null && f.HasGoodwill) ? f.PlayerGoodwill : 0,
                    relation = playerFaction != null ? f.PlayerRelationKind.ToString() : "None",
                    kind = f.def.categoryTag ?? ""
                })
                .ToList();

            return Task.FromResult(Result<string, RimMindError>.Ok(
                JsonConvert.SerializeObject(factions ?? new object())));
        }

        public override Task<Result<bool, RimMindError>> ExecuteSetAsync(MechanismWriteArgs args, CancellationToken ct)
        {
            if (!int.TryParse(ExtractParam(args, "target_faction_id"), out var targetFactionId))
                return Task.FromResult(Result<bool, RimMindError>.Err(RimMindErrors.InvalidAction(MechanismId, "missing target_faction_id")));

            if (!int.TryParse(ExtractParam(args, "goodwill_change"), out var goodwillChange))
                return Task.FromResult(Result<bool, RimMindError>.Err(RimMindErrors.InvalidAction(MechanismId, "missing goodwill_change")));

            var targetFaction = Find.FactionManager?.AllFactions?
                .FirstOrDefault(f => f.loadID == targetFactionId);
            if (targetFaction == null)
                return Task.FromResult(Result<bool, RimMindError>.Err(RimMindErrors.InvalidDefName(targetFactionId.ToString())));

            var playerFaction = global::RimWorld.Faction.OfPlayer;
            if (playerFaction == null)
                return Task.FromResult(Result<bool, RimMindError>.Err(RimMindErrors.Internal("Player faction not found")));

            targetFaction.TryAffectGoodwillWith(playerFaction, goodwillChange, true, true);
            return Task.FromResult(Result<bool, RimMindError>.Ok(true));
        }

        public override Task<Result<IReadOnlyList<MechanismEnumResult>, RimMindError>> ExecuteListAsync(int? pawnId, CancellationToken ct)
        {
            var results = Find.FactionManager?.AllFactions?
                .Where(f => f?.def != null && !f.def.hidden && !f.IsPlayer)
                .Select(f => new MechanismEnumResult
                {
                    DefName = f.def.defName,
                    Label = f.Name ?? f.def.label ?? f.def.defName,
                    Description = f.def.description
                })
                .ToList() ?? new List<MechanismEnumResult>();

            return Task.FromResult(Result<IReadOnlyList<MechanismEnumResult>, RimMindError>.Ok(results.AsReadOnly()));
        }

        private static string? ExtractParam(MechanismWriteArgs args, string key)
        {
            if (args.Params != null && args.Params.TryGetValue(key, out var val))
                return val;
            return null;
        }
    }
}
