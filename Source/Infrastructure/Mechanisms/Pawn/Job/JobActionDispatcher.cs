using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
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
        [ThreadStatic]
        private static bool _captureJobForQueue;

        [ThreadStatic]
        private static VerseJob? _capturedQueueJob;

        /// <summary>
        /// Starts a forced job and verifies the game actually accepted it. StartJob can silently
        /// refuse (mental state, drafted pawn, failed reservation, job ending on its first toil);
        /// reporting Ok in that case is a fake ToolCall success that misleads the agent loop.
        /// </summary>
        public static Result<bool, RimMindError> StartJobVerified(VersePawn pawn, VerseJob job, string mechanismId = "pawn.job")
        {
            if (pawn?.jobs == null || job == null)
                return Result<bool, RimMindError>.Err(RimMindErrors.InvalidAction(mechanismId, "no job tracker or job"));
            if (_captureJobForQueue)
            {
                _capturedQueueJob = job;
                return Result<bool, RimMindError>.Ok(true);
            }
            pawn.jobs.StartJob(job, JobCondition.InterruptForced);
            if (!ReferenceEquals(pawn.CurJob, job))
            {
                return Result<bool, RimMindError>.Err(RimMindErrors.InvalidAction(
                    mechanismId,
                    $"game did not start job '{job.def?.defName}' for {pawn.LabelShort} (current job: {pawn.CurJobDef?.defName ?? "none"})"));
            }
            return Result<bool, RimMindError>.Ok(true);
        }
        public static Result<bool, RimMindError> HandleAssignWork(VersePawn pawn, MechanismWriteArgs args)
        {
            var workType = ExtractParam(args, "work_type") ?? ExtractParam(args, "job_type") ?? args.DefName;
            if (string.IsNullOrEmpty(workType))
            {
                // No explicit work type: let the pawn pick its highest-priority available work
                // through the vanilla work scanner instead of failing the whole decision.
                VerseJob? anyWork = null;
                try
                {
                    anyWork = new JobGiver_Work().TryIssueJobPackage(pawn, default).Job;
                }
                catch
                {
                    anyWork = null;
                }
                if (anyWork == null)
                    return Result<bool, RimMindError>.Err(RimMindErrors.InvalidAction("pawn.job", "assign_work: missing work_type and no available work found"));
                return StartJobVerified(pawn, anyWork);
            }

            var candidateGivers = ResolveCandidateWorkGivers(workType!);
            var workTypeDef = ResolveWorkTypeDef(workType!, candidateGivers);
            if (candidateGivers.Count == 0 && workTypeDef == null)
                return Result<bool, RimMindError>.Err(RimMindErrors.InvalidDefName(workType ?? ""));

            bool priorityUpdated = false;
            if (workTypeDef != null
                && pawn.workSettings != null
                && pawn.workSettings.EverWork
                && !pawn.WorkTypeIsDisabled(workTypeDef)
                && RimMind.Presentation.Api.RimMindAPI.Autonomy.CanExecuteAutonomous(
                    pawn,
                    RimMind.Domain.Agent.Policies.AutonomyDomain.WorkPriorityModification))
            {
                pawn.workSettings.EnableAndInitializeIfNotAlreadyInitialized();
                pawn.workSettings.SetPriority(workTypeDef, 1);
                priorityUpdated = true;
            }

            foreach (var workGiver in candidateGivers)
            {
                if (workGiver?.Worker == null) continue;
                if (workGiver.workType != null && pawn.WorkTypeIsDisabled(workGiver.workType)) continue;

                var job = TryFindJobFromGiver(pawn, workGiver);
                if (job != null)
                {
                    job.workGiverDef = workGiver;
                    return StartJobVerified(pawn, job);
                }
            }

            if (!_captureJobForQueue && (priorityUpdated || (workTypeDef != null && !pawn.WorkTypeIsDisabled(workTypeDef))))
            {
                try
                {
                    pawn.jobs?.CheckForJobOverride();
                }
                catch
                {
                    // Ignore job override transient errors
                }
                return Result<bool, RimMindError>.Ok(true);
            }

            return Result<bool, RimMindError>.Err(RimMindErrors.Internal($"No actionable target on map for work/job '{workType}'"));
        }

        private static WorkTypeDef? ResolveWorkTypeDef(string rawType, System.Collections.Generic.List<WorkGiverDef> candidateGivers)
        {
            var trimmed = rawType.Trim();
            var direct = DefDatabase<WorkTypeDef>.GetNamedSilentFail(trimmed);
            if (direct != null) return direct;

            foreach (var wt in DefDatabase<WorkTypeDef>.AllDefsListForReading)
            {
                if (wt == null) continue;
                if (string.Equals(wt.defName, trimmed, System.StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(wt.labelShort, trimmed, System.StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(wt.gerundLabel, trimmed, System.StringComparison.OrdinalIgnoreCase))
                {
                    return wt;
                }
            }

            return candidateGivers.FirstOrDefault(g => g?.workType != null)?.workType;
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
            if (workGiver?.Worker == null) return null;
            VerseJob? job = null;
            try
            {
                if (workGiver.Worker is WorkGiver_Scanner scanner)
                {
                    if (scanner.ShouldSkip(pawn, forced: true) || scanner.MissingRequiredCapacity(pawn) != null)
                        return null;

                    job = scanner.NonScanJob(pawn);
                    if (job == null && scanner.def.scanThings)
                    {
                        var potentialThings = scanner.PotentialWorkThingsGlobal(pawn);
                        if (potentialThings != null)
                        {
                            foreach (var t in potentialThings)
                            {
                                if (t != null && !t.IsForbidden(pawn) && scanner.HasJobOnThing(pawn, t, forced: true) && pawn.CanReach(t, scanner.PathEndMode, Danger.Deadly))
                                {
                                    job = scanner.JobOnThing(pawn, t, forced: true);
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
                                t => !t.IsForbidden(pawn) && scanner.HasJobOnThing(pawn, t, forced: true));
                            if (target != null)
                            {
                                job = scanner.JobOnThing(pawn, target, forced: true);
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
                                if (!c.IsForbidden(pawn) && scanner.HasJobOnCell(pawn, c, forced: true) && pawn.CanReach(c, scanner.PathEndMode, Danger.Deadly))
                                {
                                    job = scanner.JobOnCell(pawn, c, forced: true);
                                    if (job != null) break;
                                }
                            }
                        }
                        else if (pawn.Map != null && scanner.HasJobOnCell(pawn, pawn.Position, forced: true))
                        {
                            job = scanner.JobOnCell(pawn, pawn.Position, forced: true);
                        }
                    }
                }
                else
                {
                    if (workGiver.Worker.ShouldSkip(pawn, forced: true) || workGiver.Worker.MissingRequiredCapacity(pawn) != null)
                        return null;
                    job = workGiver.Worker.NonScanJob(pawn);
                }

                if (job != null)
                {
                    job.workGiverDef ??= workGiver;
                }
            }
            catch
            {
                return null;
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
            return StartJobVerified(pawn, job);
        }

        public static Result<bool, RimMindError> HandleEatFood(VersePawn pawn, MechanismWriteArgs args)
        {
            var food = pawn.inventory?.innerContainer?.FirstOrDefault(
                t => t != null && !t.Destroyed && t.def != null && t.def.IsNutritionGivingIngestible && t.IngestibleNow);
            var map = pawn.Map ?? Find.CurrentMap;
            if (food == null && map != null && map.resourceCounter?.AllCountedAmounts != null)
            {
                foreach (var entry in map.resourceCounter.AllCountedAmounts)
                {
                    if (entry.Key != null && entry.Key.IsNutritionGivingIngestible && entry.Value > 0)
                    {
                        food = map.listerThings?.ThingsOfDef(entry.Key)?.FirstOrDefault(
                            t => t != null && !t.Destroyed && t.Spawned && !t.IsForbidden(pawn) && t.IngestibleNow
                                 && pawn.CanReserveAndReach(t, PathEndMode.OnCell, Danger.Deadly));
                        if (food != null) break;
                    }
                }
            }

            if (food == null)
            {
                try
                {
                    if (FoodUtility.TryFindBestFoodSourceFor(
                        pawn,
                        pawn,
                        desperate: true,
                        out var bestFood,
                        out _,
                        canRefillDispenser: true,
                        canUseInventory: true,
                        allowForbidden: false) && bestFood != null && !bestFood.Destroyed)
                    {
                        food = bestFood;
                    }
                }
                catch
                {
                    // Fallback to direct map scan below
                }
            }

            if (food == null && map?.listerThings?.AllThings != null)
            {
                float bestDistSq = float.MaxValue;
                var allThings = map.listerThings.AllThings;
                for (int i = 0; i < allThings.Count; i++)
                {
                    var t = allThings[i];
                    if (t == null || t.Destroyed || !t.Spawned || t.def == null || !t.def.IsNutritionGivingIngestible)
                        continue;
                    if (t.IsForbidden(pawn) || !t.IngestibleNow)
                        continue;
                    if (!pawn.CanReserveAndReach(t, PathEndMode.OnCell, Danger.Deadly))
                        continue;

                    float distSq = (t.Position - pawn.Position).LengthHorizontalSquared;
                    if (distSq < bestDistSq)
                    {
                        bestDistSq = distSq;
                        food = t;
                    }
                }
            }

            if (food == null && map != null)
            {
                var harvestPlantJob = TryDesignateAndHarvestWildFoodPlant(pawn, map);
                if (harvestPlantJob != null)
                {
                    return StartJobVerified(pawn, harvestPlantJob);
                }
            }

            if (food == null)
            {
                return Result<bool, RimMindError>.Err(RimMindErrors.InvalidAction("pawn.job", "eat_food: no available food found"));
            }

            var job = JobMaker.MakeJob(JobDefOf.Ingest, new LocalTargetInfo(food));
            job.count = System.Math.Max(1, FoodUtility.WillIngestStackCountOf(pawn, food.def, food.GetStatValue(StatDefOf.Nutrition)));
            return StartJobVerified(pawn, job);
        }

        private static VerseJob? TryDesignateAndHarvestWildFoodPlant(VersePawn pawn, global::Verse.Map map)
        {
            if (map.listerThings == null || map.designationManager == null)
                return null;

            var plants = map.listerThings.ThingsInGroup(ThingRequestGroup.Plant);
            if (plants == null || plants.Count == 0)
                return null;

            Plant? nearestPlant = null;
            float bestDistSq = float.MaxValue;
            int designatedCount = 0;

            for (int i = 0; i < plants.Count; i++)
            {
                if (plants[i] is not Plant plant || plant.Destroyed || !plant.Spawned || plant.def?.plant == null)
                    continue;
                if (plant.def.plant.IsTree || !plant.def.plant.Harvestable || !plant.HarvestableNow)
                    continue;

                var harvestedDef = plant.def.plant.harvestedThingDef;
                if (harvestedDef == null || !harvestedDef.IsNutritionGivingIngestible || harvestedDef.ingestible == null || !harvestedDef.ingestible.HumanEdible)
                    continue;
                if (plant.IsForbidden(pawn) || !pawn.CanReserveAndReach(plant, PathEndMode.Touch, Danger.Deadly))
                    continue;

                if (designatedCount < 10 && map.designationManager.DesignationOn(plant, DesignationDefOf.HarvestPlant) == null)
                {
                    map.designationManager.AddDesignation(new Designation(plant, DesignationDefOf.HarvestPlant));
                    designatedCount++;
                }

                float distSq = (plant.Position - pawn.Position).LengthHorizontalSquared;
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    nearestPlant = plant;
                }
            }

            if (nearestPlant != null && !pawn.WorkTagIsDisabled(WorkTags.PlantWork))
            {
                if (pawn.workSettings != null
                    && pawn.workSettings.EverWork
                    && !pawn.WorkTypeIsDisabled(WorkTypeDefOf.PlantCutting)
                    && RimMind.Presentation.Api.RimMindAPI.Autonomy.CanExecuteAutonomous(
                        pawn,
                        RimMind.Domain.Agent.Policies.AutonomyDomain.WorkPriorityModification))
                {
                    pawn.workSettings.EnableAndInitializeIfNotAlreadyInitialized();
                    pawn.workSettings.SetPriority(WorkTypeDefOf.PlantCutting, 1);
                }

                var plantsCutGiver = DefDatabase<WorkGiverDef>.GetNamedSilentFail("PlantsCut");
                if (plantsCutGiver?.Worker is WorkGiver_Scanner scanner)
                {
                    try
                    {
                        if (scanner.HasJobOnThing(pawn, nearestPlant, forced: true))
                        {
                            var giverJob = scanner.JobOnThing(pawn, nearestPlant, forced: true);
                            if (giverJob != null)
                            {
                                giverJob.workGiverDef = plantsCutGiver;
                                return giverJob;
                            }
                        }
                    }
                    catch
                    {
                        // Fallback to direct HarvestDesignated job on nearestPlant
                    }
                }

                var fallbackHarvestJob = JobMaker.MakeJob(JobDefOf.HarvestDesignated, new LocalTargetInfo(nearestPlant));
                fallbackHarvestJob.workGiverDef = plantsCutGiver;
                return fallbackHarvestJob;
            }

            return null;
        }

        public static Result<bool, RimMindError> HandleForceRest(VersePawn pawn, MechanismWriteArgs args)
        {
            var bed = RestUtility.FindBedFor(pawn);
            var job = bed != null ? JobMaker.MakeJob(JobDefOf.LayDown, new LocalTargetInfo(bed)) : JobMaker.MakeJob(JobDefOf.LayDown, pawn.Position);
            return StartJobVerified(pawn, job);
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
            return StartJobVerified(pawn, job);
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
            return StartJobVerified(pawn, job);
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
            return StartJobVerified(pawn, job);
        }

        public static Result<bool, RimMindError> HandleCancelJob(VersePawn pawn, MechanismWriteArgs args)
        {
            if (pawn.jobs.curJob != null)
            {
                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
            }
            return Result<bool, RimMindError>.Ok(true);
        }

        public static Result<bool, RimMindError> HandleEnqueueJob(VersePawn pawn, MechanismWriteArgs args)
        {
            if (pawn?.jobs == null)
                return Result<bool, RimMindError>.Err(RimMindErrors.InvalidAction("pawn.job", "enqueue_job: pawn has no job tracker"));

            string subAction = ResolveQueueSubAction(args);
            if (string.IsNullOrEmpty(subAction))
            {
                return Result<bool, RimMindError>.Err(RimMindErrors.InvalidAction(
                    "pawn.job",
                    "enqueue_job: specify job_action (assign_work, move_to, eat_food, force_rest, tend_pawn, rescue_pawn) or work_type / cell_x"));
            }

            if (!TryBuildJobWithoutStarting(pawn, subAction, args, out var job, out var buildError) || job == null)
            {
                return Result<bool, RimMindError>.Err(buildError ?? RimMindErrors.InvalidAction("pawn.job", $"enqueue_job: failed to build job for '{subAction}'"));
            }

            job.playerForced = true;
            if (pawn.jobs.curJob == null)
                return StartJobVerified(pawn, job);

            string? queuePosition = ExtractParam(args, "queue_position");
            if (string.Equals(queuePosition, "first", StringComparison.OrdinalIgnoreCase))
                pawn.jobs.jobQueue.EnqueueFirst(job);
            else
                pawn.jobs.jobQueue.EnqueueLast(job);

            return Result<bool, RimMindError>.Ok(true);
        }

        public static Result<bool, RimMindError> HandleQueueJobChain(VersePawn pawn, MechanismWriteArgs args)
        {
            if (pawn?.jobs == null)
                return Result<bool, RimMindError>.Err(RimMindErrors.InvalidAction("pawn.job", "queue_job_chain: pawn has no job tracker"));

            if (!TryExtractJobChainArray(args, out var array, out var parseError) || array == null || array.Count == 0)
            {
                return Result<bool, RimMindError>.Err(parseError ?? RimMindErrors.InvalidAction(
                    "pawn.job",
                    "queue_job_chain: provide a non-empty JSON array of job steps in jobs_json or value_json"));
            }

            const int MaxChainSteps = 6;
            int stepCount = Math.Min(array.Count, MaxChainSteps);
            var builtJobs = new List<VerseJob>(stepCount);

            for (int i = 0; i < stepCount; i++)
            {
                if (array[i] is not JObject stepObj)
                {
                    return Result<bool, RimMindError>.Err(RimMindErrors.InvalidAction(
                        "pawn.job",
                        $"queue_job_chain: step[{i}] must be a JSON object"));
                }

                var stepParams = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var prop in stepObj.Properties())
                {
                    if (prop.Value == null || prop.Value.Type == JTokenType.Null)
                        continue;
                    if (prop.Value.Type == JTokenType.Object || prop.Value.Type == JTokenType.Array)
                        stepParams[prop.Name] = prop.Value.ToString(Newtonsoft.Json.Formatting.None);
                    else
                        stepParams[prop.Name] = prop.Value.ToString();
                }

                var stepArgs = new MechanismWriteArgs
                {
                    MechanismId = args.MechanismId,
                    PawnId = args.PawnId,
                    MapId = args.MapId,
                    Action = stepObj["action"]?.ToString() ?? stepObj["job_action"]?.ToString() ?? stepObj["sub_action"]?.ToString() ?? string.Empty,
                    DefName = stepObj["def_name"]?.ToString() ?? stepObj["work_type"]?.ToString(),
                    Params = stepParams
                };

                string subAction = ResolveQueueSubAction(stepArgs);
                if (string.IsNullOrEmpty(subAction))
                {
                    return Result<bool, RimMindError>.Err(RimMindErrors.InvalidAction(
                        "pawn.job",
                        $"queue_job_chain: step[{i}] could not resolve job action (set action, work_type, or cell_x)"));
                }

                if (!TryBuildJobWithoutStarting(pawn, subAction, stepArgs, out var job, out var buildError) || job == null)
                {
                    return Result<bool, RimMindError>.Err(RimMindErrors.InvalidAction(
                        "pawn.job",
                        $"queue_job_chain: step[{i}] ({subAction}) failed: {buildError?.Message ?? "no valid job"}"));
                }

                job.playerForced = true;
                builtJobs.Add(job);
            }

            if (builtJobs.Count == 0)
            {
                return Result<bool, RimMindError>.Err(RimMindErrors.InvalidAction("pawn.job", "queue_job_chain: no valid jobs built"));
            }

            int enqueueStartIndex = 0;
            if (pawn.jobs.curJob == null)
            {
                var startRes = StartJobVerified(pawn, builtJobs[0]);
                if (startRes.IsErr)
                    return startRes;
                enqueueStartIndex = 1;
            }

            for (int i = enqueueStartIndex; i < builtJobs.Count; i++)
            {
                pawn.jobs.jobQueue.EnqueueLast(builtJobs[i]);
            }

            return Result<bool, RimMindError>.Ok(true);
        }

        public static Result<bool, RimMindError> HandleRemoveQueuedJob(VersePawn pawn, MechanismWriteArgs args)
        {
            var queue = pawn?.jobs?.jobQueue;
            if (queue == null || queue.Count == 0)
                return Result<bool, RimMindError>.Err(RimMindErrors.InvalidAction("pawn.job", "remove_queued_job: job queue is empty"));

            int index = int.TryParse(ExtractParam(args, "job_index"), out var parsedIndex) ? parsedIndex : 0;
            if (index < 0 || index >= queue.Count)
                return Result<bool, RimMindError>.Err(RimMindErrors.InvalidAction("pawn.job", $"remove_queued_job: job_index {index} out of range (0..{queue.Count - 1})"));

            var targetJob = queue[index]?.job;
            if (targetJob == null)
                return Result<bool, RimMindError>.Err(RimMindErrors.InvalidAction("pawn.job", $"remove_queued_job: queued job at index {index} is null"));

            queue.Extract(targetJob);
            return Result<bool, RimMindError>.Ok(true);
        }

        public static Result<bool, RimMindError> HandleClearQueue(VersePawn pawn, MechanismWriteArgs args)
        {
            if (pawn?.jobs == null)
                return Result<bool, RimMindError>.Err(RimMindErrors.InvalidAction("pawn.job", "clear_queue: pawn has no job tracker"));

            pawn.jobs.ClearQueuedJobs(true);
            return Result<bool, RimMindError>.Ok(true);
        }

        private static string ResolveQueueSubAction(MechanismWriteArgs args)
        {
            string? explicitSub = ExtractParam(args, "job_action") ?? ExtractParam(args, "sub_action");
            if (!string.IsNullOrWhiteSpace(explicitSub))
                return explicitSub!.Trim().ToLowerInvariant();

            if (!string.IsNullOrWhiteSpace(args.Action)
                && !string.Equals(args.Action, "enqueue_job", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(args.Action, "queue_job_chain", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(args.Action, "set", StringComparison.OrdinalIgnoreCase))
            {
                return args.Action.Trim().ToLowerInvariant();
            }

            if (!string.IsNullOrWhiteSpace(args.DefName)
                || !string.IsNullOrWhiteSpace(ExtractParam(args, "work_type"))
                || !string.IsNullOrWhiteSpace(ExtractParam(args, "job_type"))
                || !string.IsNullOrWhiteSpace(ExtractParam(args, "def_name")))
            {
                return "assign_work";
            }

            if (!string.IsNullOrWhiteSpace(ExtractParam(args, "cell_x"))
                || !string.IsNullOrWhiteSpace(ExtractParam(args, "x")))
            {
                return "move_to";
            }

            return string.Empty;
        }

        private static bool TryExtractJobChainArray(MechanismWriteArgs args, out JArray? array, out RimMindError? error)
        {
            array = null;
            error = null;

            string? rawJson = ExtractParam(args, "jobs_json") ?? args.ValueJson;
            if (string.IsNullOrWhiteSpace(rawJson) && !string.IsNullOrWhiteSpace(args.RawArgumentsJson))
            {
                try
                {
                    var root = JObject.Parse(args.RawArgumentsJson!);
                    var token = root["jobs"] ?? root["jobs_json"] ?? root["value_json"];
                    if (token is JArray directArray)
                    {
                        array = directArray;
                        return true;
                    }
                    rawJson = token?.ToString();
                }
                catch (Exception ex)
                {
                    error = RimMindErrors.InvalidAction("pawn.job", $"queue_job_chain: invalid RawArgumentsJson ({ex.Message})");
                    return false;
                }
            }

            if (string.IsNullOrWhiteSpace(rawJson))
                return false;

            try
            {
                var parsed = JToken.Parse(rawJson!);
                if (parsed is JArray arr)
                {
                    array = arr;
                    return true;
                }
                if (parsed is JObject obj && obj["jobs"] is JArray nestedArr)
                {
                    array = nestedArr;
                    return true;
                }
                error = RimMindErrors.InvalidAction("pawn.job", "queue_job_chain: jobs payload must be a JSON array");
                return false;
            }
            catch (Exception ex)
            {
                error = RimMindErrors.InvalidAction("pawn.job", $"queue_job_chain: invalid JSON array ({ex.Message})");
                return false;
            }
        }

        private static bool TryBuildJobWithoutStarting(
            VersePawn pawn,
            string subAction,
            MechanismWriteArgs subArgs,
            out VerseJob? builtJob,
            out RimMindError? error)
        {
            builtJob = null;
            error = null;

            bool prevCapture = _captureJobForQueue;
            VerseJob? prevJob = _capturedQueueJob;
            try
            {
                _captureJobForQueue = true;
                _capturedQueueJob = null;

                Result<bool, RimMindError> res = subAction switch
                {
                    "assign_work" or "take_job" => HandleAssignWork(pawn, subArgs),
                    "move_to" => HandleMoveTo(pawn, subArgs),
                    "eat_food" => HandleEatFood(pawn, subArgs),
                    "force_rest" => HandleForceRest(pawn, subArgs),
                    "tend_pawn" => HandleTendPawn(pawn, subArgs),
                    "rescue_pawn" => HandleRescuePawn(pawn, subArgs),
                    "arrest_pawn" => HandleArrestPawn(pawn, subArgs),
                    _ => Result<bool, RimMindError>.Err(RimMindErrors.InvalidAction("pawn.job", $"unsupported queued sub-action '{subAction}'"))
                };

                if (res.IsOk && _capturedQueueJob != null)
                {
                    builtJob = _capturedQueueJob;
                    return true;
                }

                error = res.IsErr
                    ? res.Error
                    : RimMindErrors.InvalidAction("pawn.job", $"failed to build queueable job for '{subAction}'");
                return false;
            }
            finally
            {
                _captureJobForQueue = prevCapture;
                _capturedQueueJob = prevJob;
            }
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
            if (int.TryParse(str, out var val)) return val;
            return Application.Features.Tools.PawnIdParser.TryParse(str, out var pawnId) ? pawnId : 0;
        }

        /// <summary>
        /// Actions whose actor may be a downed colonist (resting in place, dropping the current job, or clearing queue).
        /// All other pawn.job actions require a conscious, non-downed colonist.
        /// </summary>
        public static bool AllowsDownedActor(string? action)
            => action == "force_rest"
               || action == "cancel_job"
               || action == "clear_queue"
               || action == "remove_queued_job";

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
