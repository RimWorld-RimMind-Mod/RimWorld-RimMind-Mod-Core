using System.Linq;
using RimMind.Application.Common.Interfaces.Mechanisms;
using RimMind.Application.Common.Models.Mechanisms;
using RimMind.Domain.ValueObjects;
using Verse;
using Verse.AI;
using RimWorld;
using VersePawn = Verse.Pawn;
using VerseJob = global::Verse.AI.Job;

namespace RimMind.Infrastructure.Mechanisms.Pawn.Job
{
    public static class JobActionDispatcher
    {
        public static Result<bool, RimMindError> HandleAssignWork(VersePawn pawn, MechanismWriteArgs args)
        {
            var workType = ExtractParam(args, "work_type") ?? ExtractParam(args, "job_type") ?? args.DefName;
            if (string.IsNullOrEmpty(workType))
                return Result<bool, RimMindError>.Err(RimMindErrors.InvalidAction("pawn.job", "assign_work: missing work_type"));

            var candidateGivers = ResolveCandidateWorkGivers(workType!);
            if (candidateGivers.Count == 0)
                return Result<bool, RimMindError>.Err(RimMindErrors.InvalidDefName(workType ?? ""));

            foreach (var workGiver in candidateGivers)
            {
                if (workGiver?.Worker == null) continue;
                if (workGiver.workType != null && pawn.WorkTypeIsDisabled(workGiver.workType)) continue;

                var job = TryFindJobFromGiver(pawn, workGiver);
                if (job != null)
                {
                    job.workGiverDef = workGiver;
                    pawn.jobs.StartJob(job, JobCondition.InterruptForced);
                    return Result<bool, RimMindError>.Ok(true);
                }
            }

            return Result<bool, RimMindError>.Err(RimMindErrors.Internal($"No actionable target on map for work/job '{workType}'"));
        }

        private static System.Collections.Generic.List<WorkGiverDef> ResolveCandidateWorkGivers(string rawType)
        {
            var list = new System.Collections.Generic.List<WorkGiverDef>();
            var trimmed = rawType.Trim();

            var direct = DefDatabase<WorkGiverDef>.GetNamedSilentFail(trimmed);
            if (direct != null)
            {
                list.Add(direct);
                return list;
            }

            string normalized = trimmed.ToLowerInvariant() switch
            {
                "haultostorage" or "haul" or "hauling" => "HaulGeneral",
                "clean" or "cleaning" or "cleanfilth" => "CleanFilth",
                "repair" => "Repair",
                "construct" or "construction" or "build" => "ConstructFinishFrames",
                "deliver" or "deliverresources" => "ConstructDeliverResourcesToBlueprints",
                "plantcut" or "cutplants" or "chopwood" => "PlantsCut",
                "harvest" => "GrowerHarvest",
                "sow" or "grow" or "growing" => "GrowerSow",
                "cook" or "cooking" => "DoBillsCook",
                "mine" or "mining" => "Mine",
                "research" or "researching" => "Research",
                "doctor" or "tend" => "DoctorTendToHumanlikes",
                _ => trimmed
            };

            var mapped = DefDatabase<WorkGiverDef>.GetNamedSilentFail(normalized);
            if (mapped != null) list.Add(mapped);

            // Also include any WorkGiverDef matching the WorkTypeDef name or prefix
            foreach (var wg in DefDatabase<WorkGiverDef>.AllDefsListForReading)
            {
                if (wg == null || list.Contains(wg)) continue;
                if (string.Equals(wg.defName, trimmed, System.StringComparison.OrdinalIgnoreCase) ||
                    (wg.workType != null && (string.Equals(wg.workType.defName, trimmed, System.StringComparison.OrdinalIgnoreCase) ||
                                             string.Equals(wg.workType.labelShort, trimmed, System.StringComparison.OrdinalIgnoreCase))))
                {
                    list.Add(wg);
                }
            }

            return list;
        }

        private static VerseJob? TryFindJobFromGiver(VersePawn pawn, WorkGiverDef workGiver)
        {
            VerseJob? job = null;
            if (workGiver.Worker is WorkGiver_Scanner scanner)
            {
                job = scanner.NonScanJob(pawn);
                if (job == null && scanner.def.scanThings)
                {
                    var potentialThings = scanner.PotentialWorkThingsGlobal(pawn);
                    if (potentialThings != null)
                    {
                        foreach (var t in potentialThings)
                        {
                            if (t != null && !t.IsForbidden(pawn) && scanner.HasJobOnThing(pawn, t) && pawn.CanReach(t, scanner.PathEndMode, Danger.Deadly))
                            {
                                job = scanner.JobOnThing(pawn, t, true);
                                if (job != null) break;
                            }
                        }
                    }
                    else if (pawn.Map != null)
                    {
                        var target = GenClosest.ClosestThingReachable(
                            pawn.Position,
                            pawn.Map,
                            scanner.PotentialWorkThingRequest,
                            scanner.PathEndMode,
                            TraverseParms.For(pawn, Danger.Deadly),
                            9999f,
                            t => !t.IsForbidden(pawn) && scanner.HasJobOnThing(pawn, t));
                        if (target != null)
                        {
                            job = scanner.JobOnThing(pawn, target, true);
                        }
                    }
                }

                if (job == null && scanner.def.scanCells)
                {
                    var potentialCells = scanner.PotentialWorkCellsGlobal(pawn);
                    if (potentialCells != null)
                    {
                        foreach (var c in potentialCells)
                        {
                            if (!c.IsForbidden(pawn) && scanner.HasJobOnCell(pawn, c) && pawn.CanReach(c, scanner.PathEndMode, Danger.Deadly))
                            {
                                job = scanner.JobOnCell(pawn, c, true);
                                if (job != null) break;
                            }
                        }
                    }
                    else if (pawn.Map != null && scanner.HasJobOnCell(pawn, pawn.Position))
                    {
                        job = scanner.JobOnCell(pawn, pawn.Position, true);
                    }
                }
            }
            else
            {
                job = workGiver.Worker.NonScanJob(pawn);
            }

            return job;
        }

        public static Result<bool, RimMindError> HandleMoveTo(VersePawn pawn, MechanismWriteArgs args)
        {
            var cellX = ExtractIntParam(args, "cell_x");
            var cellZ = ExtractIntParam(args, "cell_z");
            if (cellX == 0 && cellZ == 0)
                return Result<bool, RimMindError>.Err(RimMindErrors.InvalidAction("pawn.job", "move_to: missing cell_x/cell_z"));

            var map = pawn.Map;
            if (map == null)
                return Result<bool, RimMindError>.Err(RimMindErrors.InvalidAction("pawn.job", "move_to: pawn has no map"));

            var cell = new IntVec3(cellX, 0, cellZ);
            if (!cell.IsValid || !cell.InBounds(map))
                return Result<bool, RimMindError>.Err(RimMindErrors.InvalidAction("pawn.job", "move_to: invalid or out of bounds cell"));

            if (cell.Fogged(map))
                return Result<bool, RimMindError>.Err(RimMindErrors.InvalidAction("pawn.job", "move_to: cell is fogged"));

            if (!cell.Standable(map))
                return Result<bool, RimMindError>.Err(RimMindErrors.InvalidAction("pawn.job", "move_to: cell is not standable"));

            if (!pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly))
                return Result<bool, RimMindError>.Err(RimMindErrors.InvalidAction("pawn.job", "move_to: cannot reach cell"));

            var job = JobMaker.MakeJob(JobDefOf.Goto, new LocalTargetInfo(cell));
            pawn.jobs.StartJob(job, JobCondition.InterruptForced);
            return Result<bool, RimMindError>.Ok(true);
        }

        public static Result<bool, RimMindError> HandleEatFood(VersePawn pawn, MechanismWriteArgs args)
        {
            var food = pawn.inventory?.innerContainer?.FirstOrDefault(
                t => t != null && !t.Destroyed && t.def.IsNutritionGivingIngestible && t.IngestibleNow);
            if (food == null)
            {
                var map = pawn.Map ?? Find.CurrentMap;
                if (map != null)
                {
                    foreach (var entry in map.resourceCounter.AllCountedAmounts)
                    {
                        if (entry.Key.IsNutritionGivingIngestible && entry.Value > 0)
                        {
                            food = map.listerThings?.ThingsOfDef(entry.Key)?.FirstOrDefault(
                                t => t != null && !t.Destroyed && t.Spawned && !t.IsForbidden(pawn) && t.IngestibleNow
                                     && pawn.CanReserveAndReach(t, PathEndMode.OnCell, Danger.Deadly));
                            if (food != null) break;
                        }
                    }
                }
            }

            if (food == null)
            {
                return Result<bool, RimMindError>.Err(RimMindErrors.InvalidAction("pawn.job", "eat_food: no available food found"));
            }

            var job = JobMaker.MakeJob(JobDefOf.Ingest, new LocalTargetInfo(food));
            job.count = System.Math.Max(1, FoodUtility.WillIngestStackCountOf(pawn, food.def, food.GetStatValue(StatDefOf.Nutrition)));
            pawn.jobs.StartJob(job, JobCondition.InterruptForced);
            return Result<bool, RimMindError>.Ok(true);
        }

        public static Result<bool, RimMindError> HandleForceRest(VersePawn pawn, MechanismWriteArgs args)
        {
            var bed = RestUtility.FindBedFor(pawn);
            var job = bed != null ? JobMaker.MakeJob(JobDefOf.LayDown, new LocalTargetInfo(bed)) : JobMaker.MakeJob(JobDefOf.LayDown, pawn.Position);
            pawn.jobs.StartJob(job, JobCondition.InterruptForced);
            return Result<bool, RimMindError>.Ok(true);
        }

        public static Result<bool, RimMindError> HandleTendPawn(VersePawn pawn, MechanismWriteArgs args)
        {
            var targetPawnId = ExtractIntParam(args, "target_pawn_id");
            var targetPawn = FindTargetPawnOnSameMap(pawn, targetPawnId);
            if (targetPawn == null)
                return Result<bool, RimMindError>.Err(RimMindErrors.PawnNotFound(targetPawnId));

            if (!pawn.CanReserveAndReach(targetPawn, PathEndMode.Touch, Danger.Deadly))
                return Result<bool, RimMindError>.Err(RimMindErrors.InvalidAction("pawn.job", "tend_pawn: cannot reserve or reach target pawn"));

            var job = JobMaker.MakeJob(JobDefOf.TendPatient, new LocalTargetInfo(targetPawn));
            job.count = 1;
            pawn.jobs.StartJob(job, JobCondition.InterruptForced);
            return Result<bool, RimMindError>.Ok(true);
        }

        public static Result<bool, RimMindError> HandleRescuePawn(VersePawn pawn, MechanismWriteArgs args)
        {
            var targetPawnId = ExtractIntParam(args, "target_pawn_id");
            var targetPawn = FindTargetPawnOnSameMap(pawn, targetPawnId);
            if (targetPawn == null)
                return Result<bool, RimMindError>.Err(RimMindErrors.PawnNotFound(targetPawnId));

            Building_Bed bed = RestUtility.FindBedFor(targetPawn, pawn, checkSocialProperness: false, ignoreOtherReservations: false, targetPawn.GuestStatus);
            if (bed == null || !pawn.CanReserveAndReach(targetPawn, PathEndMode.OnCell, Danger.Deadly))
                return Result<bool, RimMindError>.Err(RimMindErrors.InvalidAction("pawn.job", "rescue_pawn: no available bed or cannot reserve/reach target pawn"));

            var job = JobMaker.MakeJob(JobDefOf.Rescue, targetPawn, bed);
            job.count = 1;
            pawn.jobs.StartJob(job, JobCondition.InterruptForced);
            return Result<bool, RimMindError>.Ok(true);
        }

        public static Result<bool, RimMindError> HandleArrestPawn(VersePawn pawn, MechanismWriteArgs args)
        {
            var targetPawnId = ExtractIntParam(args, "target_pawn_id");
            var targetPawn = FindTargetPawnOnSameMap(pawn, targetPawnId);
            if (targetPawn == null)
                return Result<bool, RimMindError>.Err(RimMindErrors.PawnNotFound(targetPawnId));

            Building_Bed bed = RestUtility.FindBedFor(targetPawn, pawn, checkSocialProperness: false, ignoreOtherReservations: false, GuestStatus.Prisoner);
            if (bed == null || !pawn.CanReserveAndReach(targetPawn, PathEndMode.OnCell, Danger.Deadly))
                return Result<bool, RimMindError>.Err(RimMindErrors.InvalidAction("pawn.job", "arrest_pawn: no available prisoner bed or cannot reserve/reach target pawn"));

            var job = JobMaker.MakeJob(JobDefOf.Arrest, targetPawn, bed);
            job.count = 1;
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

        /// <summary>
        /// Actions whose actor may be a downed colonist (resting in place, dropping the current job).
        /// All other pawn.job actions require a conscious, non-downed colonist.
        /// </summary>
        public static bool AllowsDownedActor(string? action)
            => action == "force_rest" || action == "cancel_job";

        /// <summary>
        /// Resolves the target of tend/rescue/arrest through the shared lookup. Targets may be any live pawn
        /// (prisoners, guests, enemies) but must be spawned on the actor's map; never scans WorldPawns.
        /// </summary>
        private static VersePawn? FindTargetPawnOnSameMap(VersePawn actor, int pawnId)
        {
            var pawn = Presentation.Api.RimMindPawnLookup.FindPawnByNumber(pawnId);
            if (pawn == null || !pawn.Spawned || pawn.Map == null || pawn.Dead || pawn.DestroyedOrNull()) return null;
            return pawn.Map == actor.Map ? pawn : null;
        }
    }
}
