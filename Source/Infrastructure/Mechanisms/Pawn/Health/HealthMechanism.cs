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

namespace RimMind.Infrastructure.Mechanisms.Pawn.Health
{
    public sealed class HealthMechanism : GameMechanismBase<HediffDef>
    {
        public override string MechanismId => "pawn.health";
        public override MechanismScope Scope => MechanismScope.Pawn;
        public override MechanismRisk Risk => MechanismRisk.Safe;
        public override IReadOnlyList<MechanismOperationType> SupportedOperations => _supportedOps;
        public override MechanismDocs Docs => _docs;

        private static readonly IReadOnlyList<MechanismOperationType> _supportedOps =
            new List<MechanismOperationType> { MechanismOperationType.Query, MechanismOperationType.List }.AsReadOnly();

        private static readonly MechanismDocs _docs = new MechanismDocs
        {
            Summary = "Query pawn health conditions and hediffs",
            QueryDescription = "Query pawn health status, hediffs, and conditions. Optionally filter by def_name.",
            ListDescription = "List all health condition definitions (HediffDef)"
        };

        public override Task<Result<string, RimMindError>> ExecuteQueryAsync(MechanismReadArgs args, CancellationToken ct)
        {
            var pawn = FindPawn(args.PawnId);
            if (pawn == null)
                return Task.FromResult(Result<string, RimMindError>.Err(RimMindErrors.PawnNotFound(args.PawnId)));

            if (!string.IsNullOrEmpty(args.DefName))
            {
                var hediff = pawn.health?.hediffSet?.hediffs?
                    .FirstOrDefault(h => h.def.defName == args.DefName);
                if (hediff == null)
                    return Task.FromResult(Result<string, RimMindError>.Err(RimMindErrors.InvalidDefName(args.DefName)));

                var info = new
                {
                    def = hediff.def.defName,
                    label = hediff.Label,
                    severity = hediff.Severity,
                    partLabel = hediff.Part?.LabelCap ?? "Whole body",
                    bleedRate = hediff.BleedRate
                };
                return Task.FromResult(Result<string, RimMindError>.Ok(JsonConvert.SerializeObject(info)));
            }

            var hediffs = pawn.health?.hediffSet?.hediffs?
                .Select(h => new
                {
                    def = h.def.defName,
                    label = h.Label,
                    severity = h.Severity,
                    partLabel = h.Part?.LabelCap ?? "Whole body",
                    bleedRate = h.BleedRate
                })
                .ToList();

            return Task.FromResult(Result<string, RimMindError>.Ok(
                JsonConvert.SerializeObject(hediffs ?? new object())));
        }
    }
}
