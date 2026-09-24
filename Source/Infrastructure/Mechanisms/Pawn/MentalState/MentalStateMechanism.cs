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

namespace RimMind.Infrastructure.Mechanisms.Pawn.MentalState
{
    public sealed class MentalStateMechanism : GameMechanismBase<MentalStateDef>
    {
        public override string MechanismId => "pawn.mental_state";
        public override MechanismScope Scope => MechanismScope.Pawn;
        public override MechanismRisk Risk => MechanismRisk.Dangerous;
        public override IReadOnlyList<MechanismOperationType> SupportedOperations => _supportedOps;
        public override MechanismDocs Docs => _docs;

        private static readonly IReadOnlyList<MechanismOperationType> _supportedOps =
            new List<MechanismOperationType> { MechanismOperationType.Query, MechanismOperationType.Trigger, MechanismOperationType.List }.AsReadOnly();

        private static readonly MechanismDocs _docs = new MechanismDocs
        {
            Summary = "Query and trigger pawn mental states",
            QueryDescription = "Query pawn's current mental state",
            TriggerDescription = "Trigger a mental state on the pawn. DANGEROUS - can cause mental breaks.",
            ListDescription = "List all mental state definitions"
        };

        public override MechanismRisk GetRiskForOperation(MechanismOperationType operation)
        {
            return operation switch
            {
                MechanismOperationType.Trigger => MechanismRisk.Dangerous,
                MechanismOperationType.Query => MechanismRisk.Safe,
                MechanismOperationType.List => MechanismRisk.Safe,
                _ => Risk
            };
        }

        public override Task<Result<string, RimMindError>> ExecuteQueryAsync(MechanismReadArgs args, CancellationToken ct)
        {
            var pawn = FindPawn(args.PawnId);
            if (pawn == null)
                return Task.FromResult(Result<string, RimMindError>.Err(RimMindErrors.PawnNotFound(args.PawnId)));

            var mentalState = pawn.MentalStateDef;
            var info = new
            {
                hasMentalState = mentalState != null,
                def = mentalState?.defName,
                label = pawn.MentalState?.def?.LabelCap
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

            var mentalStateDef = FindDef(args.DefName);
            if (mentalStateDef == null)
                return Task.FromResult(Result<bool, RimMindError>.Err(RimMindErrors.InvalidDefName(args.DefName)));

            pawn.mindState?.mentalStateHandler?.TryStartMentalState(mentalStateDef, "Triggered by AI", true);
            return Task.FromResult(Result<bool, RimMindError>.Ok(true));
        }
    }
}
