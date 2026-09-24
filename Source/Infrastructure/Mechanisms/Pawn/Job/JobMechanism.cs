using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using RimMind.Application.Common.Interfaces.Mechanisms;
using RimMind.Application.Common.Models.Mechanisms;
using RimMind.Domain.Enums;
using RimMind.Domain.ValueObjects;
using Verse;

namespace RimMind.Infrastructure.Mechanisms.Pawn.Job
{
    public sealed class JobMechanism : GameMechanismBaseNoDef
    {
        public override string MechanismId => "pawn.job";
        public override MechanismScope Scope => MechanismScope.Pawn;
        public override MechanismRisk Risk => MechanismRisk.Moderate;
        public override IReadOnlyList<MechanismOperationType> SupportedOperations => _supportedOps;
        public override MechanismDocs Docs => JobDocs.Value;

        private static readonly IReadOnlyList<MechanismOperationType> _supportedOps =
            new List<MechanismOperationType> { MechanismOperationType.Query, MechanismOperationType.Set }.AsReadOnly();

        private static readonly IReadOnlyList<MechanismActionInfo> _writeActions =
            new List<MechanismActionInfo>
            {
                new MechanismActionInfo { Action = "assign_work", Description = "Assign work by WorkGiver def", DefNameHint = "WorkGiverDef", RequiredParams = new List<string> { "work_type" }.AsReadOnly() },
                new MechanismActionInfo { Action = "move_to", Description = "Move pawn to a cell", RequiredParams = new List<string> { "cell_x", "cell_z" }.AsReadOnly() },
                new MechanismActionInfo { Action = "eat_food", Description = "Make pawn eat food" },
                new MechanismActionInfo { Action = "force_rest", Description = "Force pawn to rest" },
                new MechanismActionInfo { Action = "tend_pawn", Description = "Tend a patient pawn", RequiredParams = new List<string> { "target_pawn_id" }.AsReadOnly() },
                new MechanismActionInfo { Action = "rescue_pawn", Description = "Rescue a downed pawn", RequiredParams = new List<string> { "target_pawn_id" }.AsReadOnly() },
                new MechanismActionInfo { Action = "arrest_pawn", Description = "Arrest a pawn", RequiredParams = new List<string> { "target_pawn_id" }.AsReadOnly() },
                new MechanismActionInfo { Action = "cancel_job", Description = "Cancel current job" }
            }.AsReadOnly();

        public override IReadOnlyList<MechanismActionInfo>? GetWriteActions() => _writeActions;

        public override Task<Result<string, RimMindError>> ExecuteQueryAsync(MechanismReadArgs args, CancellationToken ct)
        {
            var pawn = FindPawn(args.PawnId);
            if (pawn == null)
                return Task.FromResult(Result<string, RimMindError>.Err(RimMindErrors.PawnNotFound(args.PawnId)));

            var curJob = pawn.jobs?.curJob;
            var info = new
            {
                currentJob = curJob != null ? new
                {
                    def = curJob.def?.defName,
                    target = curJob.targetA.Thing?.LabelCap ?? curJob.targetA.Cell.ToString(),
                    isForced = curJob.playerForced
                } : null,
                jobQueueCount = pawn.jobs?.jobQueue?.Count ?? 0
            };

            return Task.FromResult(Result<string, RimMindError>.Ok(JsonConvert.SerializeObject(info)));
        }

        public override Task<Result<bool, RimMindError>> ExecuteSetAsync(MechanismWriteArgs args, CancellationToken ct)
        {
            var pawn = FindPawn(args.PawnId);
            if (pawn == null)
                return Task.FromResult(Result<bool, RimMindError>.Err(RimMindErrors.PawnNotFound(args.PawnId)));

            var result = args.Action switch
            {
                "assign_work" => JobActionDispatcher.HandleAssignWork(pawn, args),
                "move_to" => JobActionDispatcher.HandleMoveTo(pawn, args),
                "eat_food" => JobActionDispatcher.HandleEatFood(pawn, args),
                "force_rest" => JobActionDispatcher.HandleForceRest(pawn, args),
                "tend_pawn" => JobActionDispatcher.HandleTendPawn(pawn, args),
                "rescue_pawn" => JobActionDispatcher.HandleRescuePawn(pawn, args),
                "arrest_pawn" => JobActionDispatcher.HandleArrestPawn(pawn, args),
                "cancel_job" => JobActionDispatcher.HandleCancelJob(pawn, args),
                _ => Result<bool, RimMindError>.Err(RimMindErrors.InvalidAction(MechanismId, args.Action))
            };

            return Task.FromResult(result);
        }
    }
}
