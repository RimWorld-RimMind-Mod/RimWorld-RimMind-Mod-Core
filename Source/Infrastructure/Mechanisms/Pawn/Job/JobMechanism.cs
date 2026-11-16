using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using RimMind.Application.Common.Interfaces.Mechanisms;
using RimMind.Application.Common.Models.Mechanisms;
using RimMind.Application.Features.Tools;
using RimMind.Domain.Enums;
using RimMind.Domain.ValueObjects;
using Verse;

namespace RimMind.Infrastructure.Mechanisms.Pawn.Job
{
    public sealed class JobMechanism : GameMechanismBaseNoDef, IMechanismActionRiskProvider
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
                new MechanismActionInfo { Action = "cancel_job", Description = "Cancel current job" },
                new MechanismActionInfo { Action = "enqueue_job", Description = "Enqueue a job after current work (supports queue_position=first|last and job_action)" },
                new MechanismActionInfo { Action = "queue_job_chain", Description = "Queue an ordered sequence of up to 6 jobs via jobs_json array" },
                new MechanismActionInfo { Action = "remove_queued_job", Description = "Remove a queued job by 0-based job_index" },
                new MechanismActionInfo { Action = "clear_queue", Description = "Clear all pending queued jobs without cancelling the current job" }
            }.AsReadOnly();

        public override IReadOnlyList<MechanismActionInfo>? GetWriteActions() => _writeActions;

        public MechanismRisk? GetRiskForAction(string action)
            => string.Equals(action, "arrest_pawn", System.StringComparison.OrdinalIgnoreCase)
                ? MechanismRisk.Dangerous
                : null;

        public override Task<Result<string, RimMindError>> ExecuteQueryAsync(MechanismReadArgs args, CancellationToken ct)
        {
            var pawn = FindPawn(args.PawnId);
            if (pawn == null)
                return Task.FromResult(Result<string, RimMindError>.Err(RimMindErrors.PawnNotFound(args.PawnId)));

            var curJob = pawn.jobs?.curJob;
            var queue = pawn.jobs?.jobQueue;
            var queuedList = new List<object>();
            if (queue != null)
            {
                for (int i = 0; i < queue.Count; i++)
                {
                    var qJob = queue[i]?.job;
                    queuedList.Add(new
                    {
                        index = i,
                        def = qJob?.def?.defName,
                        target = qJob?.targetA.Thing?.LabelCap ?? qJob?.targetA.Cell.ToString(),
                        isForced = qJob?.playerForced ?? false
                    });
                }
            }

            var info = new
            {
                currentJob = curJob != null ? new
                {
                    def = curJob.def?.defName,
                    target = curJob.targetA.Thing?.LabelCap ?? curJob.targetA.Cell.ToString(),
                    isForced = curJob.playerForced
                } : null,
                jobQueueCount = queue?.Count ?? 0,
                queuedJobs = queuedList
            };

            return Task.FromResult(Result<string, RimMindError>.Ok(JsonConvert.SerializeObject(info)));
        }

        public override Task<Result<bool, RimMindError>> ExecuteSetAsync(MechanismWriteArgs args, CancellationToken ct)
        {
            string effectiveAction = ResolveEffectiveAction(args);
            if (string.IsNullOrEmpty(effectiveAction))
                return Task.FromResult(Result<bool, RimMindError>.Err(RimMindErrors.InvalidAction(MechanismId, MissingActionMessage)));
            var pawn = FindWritableColonist(args.PawnId, allowDowned: JobActionDispatcher.AllowsDownedActor(effectiveAction), out var pawnError);
            if (pawn == null)
                return Task.FromResult(Result<bool, RimMindError>.Err(pawnError!));

            var result = effectiveAction switch
            {
                "assign_work" or "take_job" => JobActionDispatcher.HandleAssignWork(pawn, args),
                "move_to" => JobActionDispatcher.HandleMoveTo(pawn, args),
                "eat_food" => JobActionDispatcher.HandleEatFood(pawn, args),
                "force_rest" => JobActionDispatcher.HandleForceRest(pawn, args),
                "tend_pawn" => JobActionDispatcher.HandleTendPawn(pawn, args),
                "rescue_pawn" => JobActionDispatcher.HandleRescuePawn(pawn, args),
                "arrest_pawn" => JobActionDispatcher.HandleArrestPawn(pawn, args),
                "cancel_job" => JobActionDispatcher.HandleCancelJob(pawn, args),
                "enqueue_job" => JobActionDispatcher.HandleEnqueueJob(pawn, args),
                "queue_job_chain" => JobActionDispatcher.HandleQueueJobChain(pawn, args),
                "remove_queued_job" => JobActionDispatcher.HandleRemoveQueuedJob(pawn, args),
                "clear_queue" => JobActionDispatcher.HandleClearQueue(pawn, args),
                _ => Result<bool, RimMindError>.Err(RimMindErrors.InvalidAction(MechanismId, effectiveAction))
            };

            return Task.FromResult(result);
        }

        internal static string ResolveEffectiveAction(MechanismWriteArgs args)
        {
            if (!string.IsNullOrWhiteSpace(args.Action) &&
                !string.Equals(args.Action, "set", System.StringComparison.OrdinalIgnoreCase))
            {
                return args.Action;
            }

            if (!string.IsNullOrWhiteSpace(args.DefName) ||
                HasNonEmptyParam(args.Params, "work_type") ||
                HasNonEmptyParam(args.Params, "job_type") ||
                HasNonEmptyParam(args.Params, "def_name"))
            {
                return "assign_work";
            }

            if (HasNonEmptyParam(args.Params, "cell_x") || HasNonEmptyParam(args.Params, "x"))
            {
                return "move_to";
            }

            // No implicit default: silently mapping an argument-less call to force_rest made
            // colonists lie down instead of eating, tending or working in live playthroughs.
            return string.Empty;
        }

        internal const string MissingActionMessage =
            "missing 'action'. Use one of: assign_work (work_type), move_to (cell_x, cell_z), eat_food, force_rest, tend_pawn (target_pawn_id), rescue_pawn (target_pawn_id), arrest_pawn (target_pawn_id), cancel_job, enqueue_job, queue_job_chain, remove_queued_job, clear_queue";

        private static bool HasNonEmptyParam(IReadOnlyDictionary<string, string>? parameters, string key)
            => parameters != null && parameters.TryGetValue(key, out var val) && !string.IsNullOrWhiteSpace(val);
    }
}
