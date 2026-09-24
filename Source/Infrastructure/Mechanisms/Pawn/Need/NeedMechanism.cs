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

namespace RimMind.Infrastructure.Mechanisms.Pawn.Need
{
    public sealed class NeedMechanism : GameMechanismBaseNoDef
    {
        public override string MechanismId => "pawn.need";
        public override MechanismScope Scope => MechanismScope.Pawn;
        public override MechanismRisk Risk => MechanismRisk.Moderate;
        public override IReadOnlyList<MechanismOperationType> SupportedOperations => _supportedOps;
        public override MechanismDocs Docs => _docs;

        private static readonly IReadOnlyList<MechanismOperationType> _supportedOps =
            new List<MechanismOperationType> { MechanismOperationType.Query, MechanismOperationType.Set, MechanismOperationType.List }.AsReadOnly();

        private static readonly MechanismDocs _docs = new MechanismDocs
        {
            Summary = "Query and modify pawn needs",
            QueryDescription = "Query pawn need levels. Optionally filter by def_name.",
            SetDescription = "Modify a pawn need level. Action: set_level (set CurLevel to a float value).",
            ListDescription = "List all need definitions available for this pawn."
        };

        public override Task<Result<string, RimMindError>> ExecuteQueryAsync(MechanismReadArgs args, CancellationToken ct)
        {
            var pawn = FindPawn(args.PawnId);
            if (pawn == null)
                return Task.FromResult(Result<string, RimMindError>.Err(RimMindErrors.PawnNotFound(args.PawnId)));

            if (!string.IsNullOrEmpty(args.DefName))
            {
                var need = pawn.needs?.AllNeeds?.FirstOrDefault(n => n.def.defName == args.DefName);
                if (need == null)
                    return Task.FromResult(Result<string, RimMindError>.Err(RimMindErrors.InvalidDefName(args.DefName)));

                var info = new { def = args.DefName, curLevel = need.CurLevel };
                return Task.FromResult(Result<string, RimMindError>.Ok(JsonConvert.SerializeObject(info)));
            }

            var needs = pawn.needs?.AllNeeds?
                .Select(n => new { def = n.def.defName, curLevel = n.CurLevel })
                .ToList();

            return Task.FromResult(Result<string, RimMindError>.Ok(
                JsonConvert.SerializeObject(needs ?? new object())));
        }

        public override Task<Result<bool, RimMindError>> ExecuteSetAsync(MechanismWriteArgs args, CancellationToken ct)
        {
            var pawn = FindPawn(args.PawnId);
            if (pawn == null)
                return Task.FromResult(Result<bool, RimMindError>.Err(RimMindErrors.PawnNotFound(args.PawnId)));

            if (string.IsNullOrEmpty(args.DefName))
                return Task.FromResult(Result<bool, RimMindError>.Err(RimMindErrors.InvalidDefName("")));

            var need = pawn.needs?.AllNeeds?.FirstOrDefault(n => n.def.defName == args.DefName);
            if (need == null)
                return Task.FromResult(Result<bool, RimMindError>.Err(RimMindErrors.InvalidDefName(args.DefName)));

            if (args.Action == "set_level" && float.TryParse(args.ValueJson, out var level))
            {
                need.CurLevel = level;
                return Task.FromResult(Result<bool, RimMindError>.Ok(true));
            }

            return Task.FromResult(Result<bool, RimMindError>.Err(RimMindErrors.InvalidAction(MechanismId, args.Action)));
        }

        public override Task<Result<IReadOnlyList<MechanismEnumResult>, RimMindError>> ExecuteListAsync(int? pawnId, CancellationToken ct)
        {
            var pawn = pawnId.HasValue ? FindPawn(pawnId.Value) : null;
            var results = pawn?.needs?.AllNeeds?
                .Select(n => new MechanismEnumResult
                {
                    DefName = n.def.defName,
                    Label = n.def.label ?? n.def.defName,
                    Description = n.def.description
                })
                .ToList() ?? new List<MechanismEnumResult>();

            return Task.FromResult(Result<IReadOnlyList<MechanismEnumResult>, RimMindError>.Ok(results.AsReadOnly()));
        }
    }
}
