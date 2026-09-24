using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using RimMind.Application.Common.Interfaces.Mechanisms;
using RimMind.Application.Common.Models.Mechanisms;
using RimMind.Domain.Enums;
using RimMind.Domain.ValueObjects;
using Verse;
using RimWorld;

namespace RimMind.Infrastructure.Mechanisms.Pawn.Inspiration
{
    public sealed class InspirationMechanism : GameMechanismBase<InspirationDef>
    {
        public override string MechanismId => "pawn.inspiration";
        public override MechanismScope Scope => MechanismScope.Pawn;
        public override MechanismRisk Risk => MechanismRisk.Moderate;
        public override IReadOnlyList<MechanismOperationType> SupportedOperations => _supportedOps;
        public override MechanismDocs Docs => _docs;

        private static readonly IReadOnlyList<MechanismOperationType> _supportedOps =
            new List<MechanismOperationType> { MechanismOperationType.Query, MechanismOperationType.Trigger, MechanismOperationType.List }.AsReadOnly();

        private static readonly MechanismDocs _docs = new MechanismDocs
        {
            Summary = "Query and trigger pawn inspirations",
            QueryDescription = "Query pawn's current inspiration status",
            TriggerDescription = "Trigger an inspiration on the pawn. Provide def_name of the InspirationDef.",
            ListDescription = "List all inspiration definitions"
        };

        public override Task<Result<string, RimMindError>> ExecuteQueryAsync(MechanismReadArgs args, CancellationToken ct)
        {
            var pawn = FindPawn(args.PawnId);
            if (pawn == null)
                return Task.FromResult(Result<string, RimMindError>.Err(RimMindErrors.PawnNotFound(args.PawnId)));

            var inspiration = pawn.mindState?.inspirationHandler?.CurState;
            var info = new
            {
                hasInspiration = inspiration != null,
                def = inspiration?.def?.defName,
                label = inspiration?.def?.LabelCap
            };

            return Task.FromResult(Result<string, RimMindError>.Ok(JsonConvert.SerializeObject(info)));
        }

        public override Task<Result<bool, RimMindError>> ExecuteTriggerAsync(MechanismWriteArgs args, CancellationToken ct)
        {
            var pawn = FindPawn(args.PawnId);
            if (pawn == null)
                return Task.FromResult(Result<bool, RimMindError>.Err(RimMindErrors.PawnNotFound(args.PawnId)));

            if (string.IsNullOrEmpty(args.DefName))
                return Task.FromResult(Result<bool, RimMindError>.Err(RimMindErrors.InvalidDefName("")));

            var inspirationDef = FindDef(args.DefName);
            if (inspirationDef == null)
                return Task.FromResult(Result<bool, RimMindError>.Err(RimMindErrors.InvalidDefName(args.DefName)));

            pawn.mindState?.inspirationHandler?.TryStartInspiration(inspirationDef);
            return Task.FromResult(Result<bool, RimMindError>.Ok(true));
        }
    }
}
