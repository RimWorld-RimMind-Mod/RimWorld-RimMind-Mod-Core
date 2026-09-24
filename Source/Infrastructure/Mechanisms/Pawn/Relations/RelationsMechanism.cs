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

namespace RimMind.Infrastructure.Mechanisms.Pawn.Relations
{
    public sealed class RelationsMechanism : GameMechanismBaseNoDef
    {
        public override string MechanismId => "pawn.relations";
        public override MechanismScope Scope => MechanismScope.Pawn;
        public override MechanismRisk Risk => MechanismRisk.Safe;
        public override IReadOnlyList<MechanismOperationType> SupportedOperations => _supportedOps;
        public override MechanismDocs Docs => _docs;

        private static readonly IReadOnlyList<MechanismOperationType> _supportedOps =
            new List<MechanismOperationType> { MechanismOperationType.Query }.AsReadOnly();

        private static readonly MechanismDocs _docs = new MechanismDocs
        {
            Summary = "Query pawn social relations and opinions",
            QueryDescription = "Query pawn's direct relations and opinion of other pawns"
        };

        public override Task<Result<string, RimMindError>> ExecuteQueryAsync(MechanismReadArgs args, CancellationToken ct)
        {
            var pawn = FindPawn(args.PawnId);
            if (pawn == null)
                return Task.FromResult(Result<string, RimMindError>.Err(RimMindErrors.PawnNotFound(args.PawnId)));

            var relations = pawn.relations?.DirectRelations?
                .Select(r => new
                {
                    def = r.def.defName,
                    label = r.def.label ?? r.def.defName,
                    otherPawnId = r.otherPawn?.thingIDNumber ?? 0,
                    otherPawnName = r.otherPawn?.LabelCap ?? "Unknown"
                })
                .ToList();

            var opinions = pawn.relations?.DirectRelations?
                .Select(r => new
                {
                    otherPawnId = r.otherPawn?.thingIDNumber ?? 0,
                    otherPawnName = r.otherPawn?.LabelCap ?? "Unknown",
                    opinion = r.otherPawn != null ? pawn.relations.OpinionOf(r.otherPawn) : 0
                })
                .GroupBy(o => o.otherPawnId)
                .Select(g => g.First())
                .ToList();

            var info = new
            {
                relations,
                opinions
            };

            return Task.FromResult(Result<string, RimMindError>.Ok(JsonConvert.SerializeObject(info)));
        }
    }
}
