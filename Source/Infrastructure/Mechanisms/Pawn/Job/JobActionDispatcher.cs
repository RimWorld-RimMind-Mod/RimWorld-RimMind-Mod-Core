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
            var job = bed != null ? JobMaker.MakeJob(JobDefOf.LayDown, new LocalTargetInfo(bed)) : JobMaker.MakeJob(JobDefOf.LayDown, pawn.Position);
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
            var pawn = Presentation.Api.RimMindPawnLookup.FindPawnByNumber(pawnId);
            if (pawn != null) return pawn;

            if (UnityData.IsInMainThread)
            {
                if (Find.Maps != null)
                {
                    foreach (var map in Find.Maps)
                    {
                        pawn = map.mapPawns?.AllPawns.FirstOrDefault(p => p.thingIDNumber == pawnId);
                        if (pawn != null)
                        {
                            Presentation.Api.RimMindPawnLookup.CachePawn(pawn);
                            return pawn;
                        }
                    }
                }
                var worldPawn = Find.WorldPawns?.AllPawnsAlive?.FirstOrDefault(p => p.thingIDNumber == pawnId);
                if (worldPawn != null)
                {
                    Presentation.Api.RimMindPawnLookup.CachePawn(worldPawn);
                    return worldPawn;
                }
            }

            return null;
        }
    }
}
