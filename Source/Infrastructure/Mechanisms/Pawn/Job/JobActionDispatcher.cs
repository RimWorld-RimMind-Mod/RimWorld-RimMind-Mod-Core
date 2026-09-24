using System.Linq;
using RimMind.Application.Common.Interfaces.Mechanisms;
using RimMind.Application.Common.Models.Mechanisms;
using RimMind.Domain.ValueObjects;
using Verse;
using Verse.AI;
using RimWorld;
using VersePawn = Verse.Pawn;

namespace RimMind.Infrastructure.Mechanisms.Pawn.Job
{
    public static class JobActionDispatcher
    {
        public static Result<bool, RimMindError> HandleAssignWork(VersePawn pawn, MechanismWriteArgs args)
        {
            var workType = ExtractParam(args, "work_type");
            if (string.IsNullOrEmpty(workType))
                return Result<bool, RimMindError>.Err(RimMindErrors.InvalidAction("pawn.job", "assign_work: missing work_type"));

            var workGiver = DefDatabase<WorkGiverDef>.GetNamedSilentFail(workType);
            if (workGiver?.Worker == null)
                return Result<bool, RimMindError>.Err(RimMindErrors.InvalidDefName(workType ?? ""));

            var scanner = workGiver.Worker as WorkGiver_Scanner;
            var job = scanner?.JobOnThing(pawn, pawn, true) ?? scanner?.JobOnCell(pawn, pawn.Position, true);
            if (job == null)
                return Result<bool, RimMindError>.Err(RimMindErrors.Internal($"No job available for work type '{workType}'"));

            pawn.jobs.StartJob(job, JobCondition.InterruptForced);
            return Result<bool, RimMindError>.Ok(true);
        }

        public static Result<bool, RimMindError> HandleMoveTo(VersePawn pawn, MechanismWriteArgs args)
        {
            var cellX = ExtractIntParam(args, "cell_x");
            var cellZ = ExtractIntParam(args, "cell_z");
            if (cellX == 0 && cellZ == 0)
                return Result<bool, RimMindError>.Err(RimMindErrors.InvalidAction("pawn.job", "move_to: missing cell_x/cell_z"));

            var cell = new IntVec3(cellX, 0, cellZ);
            if (!cell.IsValid || !cell.InBounds(Find.CurrentMap))
                return Result<bool, RimMindError>.Err(RimMindErrors.InvalidAction("pawn.job", "move_to: invalid cell"));

            var job = JobMaker.MakeJob(JobDefOf.Goto, new LocalTargetInfo(cell));
            pawn.jobs.StartJob(job, JobCondition.InterruptForced);
            return Result<bool, RimMindError>.Ok(true);
        }

        public static Result<bool, RimMindError> HandleEatFood(VersePawn pawn, MechanismWriteArgs args)
        {
            var food = pawn.inventory?.innerContainer?.FirstOrDefault(t => t.def.IsNutritionGivingIngestible);
            if (food == null)
            {
                var map = Find.CurrentMap;
                if (map != null)
                {
                    foreach (var entry in map.resourceCounter.AllCountedAmounts)
                    {
                        if (entry.Key.IsNutritionGivingIngestible && entry.Value > 0)
                        {
                            food = map.listerThings?.ThingsOfDef(entry.Key)?.FirstOrDefault();
                            if (food != null) break;
                        }
                    }
                }
            }

            var job = food != null ? JobMaker.MakeJob(JobDefOf.Ingest, new LocalTargetInfo(food)) : JobMaker.MakeJob(JobDefOf.Ingest);
            pawn.jobs.StartJob(job, JobCondition.InterruptForced);
            return Result<bool, RimMindError>.Ok(true);
        }

        public static Result<bool, RimMindError> HandleForceRest(VersePawn pawn, MechanismWriteArgs args)
        {
            var bed = RestUtility.FindBedFor(pawn);
            var job = bed != null ? JobMaker.MakeJob(JobDefOf.LayDown, new LocalTargetInfo(bed)) : JobMaker.MakeJob(JobDefOf.LayDown, pawn);
            pawn.jobs.StartJob(job, JobCondition.InterruptForced);
            return Result<bool, RimMindError>.Ok(true);
        }

        public static Result<bool, RimMindError> HandleTendPawn(VersePawn pawn, MechanismWriteArgs args)
        {
            var targetPawnId = ExtractIntParam(args, "target_pawn_id");
            var targetPawn = FindPawnById(targetPawnId);
            if (targetPawn == null)
                return Result<bool, RimMindError>.Err(RimMindErrors.PawnNotFound(targetPawnId));

            var job = JobMaker.MakeJob(JobDefOf.TendPatient, new LocalTargetInfo(targetPawn));
            pawn.jobs.StartJob(job, JobCondition.InterruptForced);
            return Result<bool, RimMindError>.Ok(true);
        }

        public static Result<bool, RimMindError> HandleRescuePawn(VersePawn pawn, MechanismWriteArgs args)
        {
            var targetPawnId = ExtractIntParam(args, "target_pawn_id");
            var targetPawn = FindPawnById(targetPawnId);
            if (targetPawn == null)
                return Result<bool, RimMindError>.Err(RimMindErrors.PawnNotFound(targetPawnId));

            var job = JobMaker.MakeJob(JobDefOf.Rescue, new LocalTargetInfo(targetPawn));
            pawn.jobs.StartJob(job, JobCondition.InterruptForced);
            return Result<bool, RimMindError>.Ok(true);
        }

        public static Result<bool, RimMindError> HandleArrestPawn(VersePawn pawn, MechanismWriteArgs args)
        {
            var targetPawnId = ExtractIntParam(args, "target_pawn_id");
            var targetPawn = FindPawnById(targetPawnId);
            if (targetPawn == null)
                return Result<bool, RimMindError>.Err(RimMindErrors.PawnNotFound(targetPawnId));

            var job = JobMaker.MakeJob(JobDefOf.Arrest, new LocalTargetInfo(targetPawn));
            pawn.jobs.StartJob(job, JobCondition.InterruptForced);
            return Result<bool, RimMindError>.Ok(true);
        }

        public static Result<bool, RimMindError> HandleCancelJob(VersePawn pawn, MechanismWriteArgs args)
        {
            if (pawn.jobs.curJob != null)
            {
                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
            }
            return Result<bool, RimMindError>.Ok(true);
        }

        private static string? ExtractParam(MechanismWriteArgs args, string key)
        {
            if (args.Params != null && args.Params.TryGetValue(key, out var val))
                return val;
            return null;
        }

        private static int ExtractIntParam(MechanismWriteArgs args, string key)
        {
            var str = ExtractParam(args, key);
            return int.TryParse(str, out var val) ? val : 0;
        }

        private static VersePawn? FindPawnById(int pawnId)
        {
            foreach (var map in Find.Maps)
            {
                var pawn = map.mapPawns?.AllPawns.FirstOrDefault(p => p.thingIDNumber == pawnId);
                if (pawn != null) return pawn;
            }
            return Find.WorldPawns?.AllPawnsAlive.FirstOrDefault(p => p.thingIDNumber == pawnId);
        }
    }
}
