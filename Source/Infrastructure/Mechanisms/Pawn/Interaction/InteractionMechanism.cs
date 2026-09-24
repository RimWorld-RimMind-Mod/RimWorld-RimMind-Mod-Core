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
using Verse.AI;
using RimWorld;

namespace RimMind.Infrastructure.Mechanisms.Pawn.Interaction
{
    public sealed class InteractionMechanism : GameMechanismBase<InteractionDef>
    {
        public override string MechanismId => "pawn.interaction";
        public override MechanismScope Scope => MechanismScope.Pawn;
        public override MechanismRisk Risk => MechanismRisk.Moderate;
        public override IReadOnlyList<MechanismOperationType> SupportedOperations => _supportedOps;
        public override MechanismDocs Docs => _docs;

        private static readonly IReadOnlyList<MechanismOperationType> _supportedOps =
            new List<MechanismOperationType> { MechanismOperationType.Query, MechanismOperationType.Trigger }.AsReadOnly();

        private static readonly MechanismDocs _docs = new MechanismDocs
        {
            Summary = "Trigger social interactions between pawns",
            QueryDescription = "Query pawn's recent interactions",
            TriggerDescription = "Trigger a social interaction. Actions: social_relax, give_item, romance_attempt, romance_breakup."
        };

        private static readonly IReadOnlyList<MechanismActionInfo> _writeActions =
            new List<MechanismActionInfo>
            {
                new MechanismActionInfo { Action = "social_relax", Description = "Trigger a social relaxation interaction" },
                new MechanismActionInfo { Action = "give_item", Description = "Give an item to another pawn", RequiredParams = new List<string> { "target_pawn_id" }.AsReadOnly() },
                new MechanismActionInfo { Action = "romance_attempt", Description = "Attempt romance with another pawn", RequiredParams = new List<string> { "target_pawn_id" }.AsReadOnly() },
                new MechanismActionInfo { Action = "romance_breakup", Description = "Break up romance with another pawn", RequiredParams = new List<string> { "target_pawn_id" }.AsReadOnly() }
            }.AsReadOnly();

        public override IReadOnlyList<MechanismActionInfo>? GetWriteActions() => _writeActions;

        public override Task<Result<string, RimMindError>> ExecuteQueryAsync(MechanismReadArgs args, CancellationToken ct)
        {
            var pawn = FindPawn(args.PawnId);
            if (pawn == null)
                return Task.FromResult(Result<string, RimMindError>.Err(RimMindErrors.PawnNotFound(args.PawnId)));

            var info = new
            {
                socialSkill = pawn.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 0,
                socialLabel = (pawn.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 0) >= 6 ? "Social" : "Non-social"
            };

            return Task.FromResult(Result<string, RimMindError>.Ok(JsonConvert.SerializeObject(info)));
        }

        public override Task<Result<bool, RimMindError>> ExecuteTriggerAsync(MechanismWriteArgs args, CancellationToken ct)
        {
            var pawn = FindPawn(args.PawnId);
            if (pawn == null)
                return Task.FromResult(Result<bool, RimMindError>.Err(RimMindErrors.PawnNotFound(args.PawnId)));

            var targetPawnId = ExtractIntParam(args, "target_pawn_id");
            var targetPawn = targetPawnId > 0 ? FindPawn(targetPawnId) : null;

            switch (args.Action)
            {
                case "social_relax":
                    var joyGiver = DefDatabase<JoyGiverDef>.AllDefsListForReading
                        .FirstOrDefault(j => j.defName == "SocialRelax");
                    if (joyGiver?.Worker != null)
                    {
                        var job = joyGiver.Worker.TryGiveJob(pawn);
                        if (job != null)
                        {
                            pawn.jobs.StartJob(job, JobCondition.InterruptForced);
                            return Task.FromResult(Result<bool, RimMindError>.Ok(true));
                        }
                    }
                    return Task.FromResult(Result<bool, RimMindError>.Err(RimMindErrors.InvalidAction(MechanismId, "social_relax: no social relax available")));

                case "give_item":
                    if (targetPawn == null)
                        return Task.FromResult(Result<bool, RimMindError>.Err(RimMindErrors.PawnNotFound(targetPawnId)));
                    var giveJob = JobMaker.MakeJob(JobDefOf.Goto, new LocalTargetInfo(targetPawn));
                    pawn.jobs.StartJob(giveJob, JobCondition.InterruptForced);
                    return Task.FromResult(Result<bool, RimMindError>.Ok(true));

                case "romance_attempt":
                    if (targetPawn == null)
                        return Task.FromResult(Result<bool, RimMindError>.Err(RimMindErrors.PawnNotFound(targetPawnId)));
                    InteractionDef romanceDef = DefDatabase<InteractionDef>.GetNamedSilentFail("RomanceAttempt");
                    if (romanceDef != null)
                    {
                        pawn.interactions?.TryInteractWith(targetPawn, romanceDef);
                        return Task.FromResult(Result<bool, RimMindError>.Ok(true));
                    }
                    return Task.FromResult(Result<bool, RimMindError>.Err(RimMindErrors.InvalidDefName("RomanceAttempt")));

                case "romance_breakup":
                    if (targetPawn == null)
                        return Task.FromResult(Result<bool, RimMindError>.Err(RimMindErrors.PawnNotFound(targetPawnId)));
                    InteractionDef breakupDef = DefDatabase<InteractionDef>.GetNamedSilentFail("Breakup");
                    if (breakupDef != null)
                    {
                        pawn.interactions?.TryInteractWith(targetPawn, breakupDef);
                        return Task.FromResult(Result<bool, RimMindError>.Ok(true));
                    }
                    return Task.FromResult(Result<bool, RimMindError>.Err(RimMindErrors.InvalidDefName("Breakup")));

                default:
                    return Task.FromResult(Result<bool, RimMindError>.Err(RimMindErrors.InvalidAction(MechanismId, args.Action)));
            }
        }

        private static int ExtractIntParam(MechanismWriteArgs args, string key)
        {
            if (args.Params != null && args.Params.TryGetValue(key, out var val) && int.TryParse(val, out var result))
                return result;
            return 0;
        }
    }
}
