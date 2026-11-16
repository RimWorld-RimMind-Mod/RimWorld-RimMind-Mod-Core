using System;
using System.Collections.Generic;
using System.Linq;
using RimMind.Presentation.Api;
using RimWorld;
using Verse;

namespace RimMind.Infrastructure.UI.DebugActions
{
    /// <summary>
    /// Command written by the external Sub Blind Agent (simulated human player) into
    /// <c>bridge/cmd_day_XX.json</c>. See <c>.agents/skills/rimmind-interactive-playthrough</c>.
    /// </summary>
    public sealed class ColonySubagentCommand
    {
        public int Day { get; set; }
        public string Action { get; set; } = string.Empty;
        public string Target { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public string UxFinding { get; set; } = string.Empty;
    }

    /// <summary>Per-colonist vitals a human player sees on screen (needs, injuries, mental state).</summary>
    public sealed class PlayerViewColonist
    {
        public string Name { get; set; } = string.Empty;
        public float Mood { get; set; }
        public float Food { get; set; }
        public float Rest { get; set; }
        public string HealthSummary { get; set; } = string.Empty;
        public bool IsDowned { get; set; }
        public float BleedingRate { get; set; }
        public bool HasTendableHediff { get; set; }
        public string MentalState { get; set; } = string.Empty;
        public string CurrentJob { get; set; } = string.Empty;
        public bool IsAgentActive { get; set; }
        public string AgentMode { get; set; } = string.Empty;
        public List<string> BadThoughts { get; set; } = new();
    }

    /// <summary>
    /// Simulated-player operations used by the in-game playthrough harness.
    /// Everything here mirrors clicks a human player would make (unforbid starting
    /// cargo, designate zones, place blueprints). It never edits pawn needs/health,
    /// never fabricates LLM traffic, and never touches RimMind Mod settings or queues.
    /// Every operation is logged with the <c>[SimulatedPlayer]</c> tag so reports keep
    /// a strict separation between the external player and Mod-internal agents.
    /// </summary>
    internal static class PlaythroughPlayerSimulation
    {
        internal const string LogTag = "[RimMind-Playthrough][SimulatedPlayer]";

        /// <summary>
        /// Crashlanded/quicktest cargo spawns forbidden; a human player unforbids it on
        /// the first frame. Without this even vanilla (non-agent) colonists starve.
        /// </summary>
        internal static int UnforbidStartingItems(Map? map)
        {
            if (map?.listerThings?.AllThings == null) return 0;

            int count = 0;
            try
            {
                var items = map.listerThings.AllThings
                    .Where(t => t != null && t.Spawned && t.def != null && t.def.category == ThingCategory.Item)
                    .ToList();

                foreach (var t in items)
                {
                    if (t.IsForbidden(Faction.OfPlayer))
                    {
                        t.SetForbidden(false, warnOnFail: false);
                        count++;
                    }
                }

                Log.Message($"{LogTag} Unforbid starting cargo: {count} item stack(s) (player action, day 1).");
            }
            catch (Exception ex)
            {
                Log.Warning($"{LogTag} UnforbidStartingItems notice: {ex.Message}");
            }

            return count;
        }

        /// <summary>
        /// Executes one player command from the bridge. Unknown actions are reported as
        /// failures; there is intentionally no scripted fallback routine.
        /// </summary>
        internal static (bool Success, string Detail) ExecuteCommand(ColonySubagentCommand cmd, Map map)
        {
            string action = (cmd.Action ?? string.Empty).Trim().ToLowerInvariant();
            IntVec3 safeBase = ResolveBaseCell(map);

            IntVec3 Offset(int dx, int dz)
            {
                var c = new IntVec3(safeBase.x + dx, safeBase.y, safeBase.z + dz);
                return c.InBounds(map) ? c : safeBase;
            }

            try
            {
                switch (action)
                {
                    case "none":
                    case "observe":
                        return (true, "observe only");

                    case "unforbid":
                        return (true, $"unforbid {UnforbidStartingItems(map)} stacks");

                    case "plan_housing":
                    {
                        int placed = 0;
                        for (int x = 0; x < 10; x++)
                        {
                            AddDesignationSafe(map, Offset(x, 0), DesignationDefOf.Plan);
                            AddDesignationSafe(map, Offset(x, 9), DesignationDefOf.Plan);
                        }
                        for (int z = 0; z < 10; z++)
                        {
                            AddDesignationSafe(map, Offset(0, z), DesignationDefOf.Plan);
                            AddDesignationSafe(map, Offset(9, z), DesignationDefOf.Plan);
                        }
                        if (TryPlaceBlueprint(map, "Bed", Offset(2, 2), Rot4.South, "WoodLog") != null) placed++;
                        if (TryPlaceBlueprint(map, "Bed", Offset(4, 2), Rot4.South, "WoodLog") != null) placed++;
                        if (TryPlaceBlueprint(map, "Bed", Offset(6, 2), Rot4.South, "WoodLog") != null) placed++;
                        if (TryPlaceBlueprint(map, "Table2x2c", Offset(5, 6), Rot4.North, "WoodLog") != null) placed++;
                        if (TryPlaceBlueprint(map, "Stool", Offset(4, 6), Rot4.East, "WoodLog") != null) placed++;
                        if (TryPlaceBlueprint(map, "TorchLamp", Offset(8, 8), Rot4.North, "WoodLog") != null) placed++;
                        return (placed > 0, $"plan lines + {placed} blueprint(s)");
                    }

                    case "mine":
                    {
                        string? targetDef = string.IsNullOrWhiteSpace(cmd.Target) ? null : cmd.Target.Trim();
                        if (targetDef != null && targetDef.IndexOf("steel", StringComparison.OrdinalIgnoreCase) >= 0)
                            targetDef = "Steel";
                        else if (targetDef != null && targetDef.IndexOf("component", StringComparison.OrdinalIgnoreCase) >= 0)
                            targetDef = "ComponentsIndustrial";

                        var mineTarget = FindNearestMineable(map, safeBase, targetDef) ?? FindNearestMineable(map, safeBase, null);
                        if (mineTarget == null) return (false, "no mineable rock found");

                        int designated = 0;
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            for (int dz = -1; dz <= 1; dz++)
                            {
                                var c = new IntVec3(mineTarget.Position.x + dx, mineTarget.Position.y, mineTarget.Position.z + dz);
                                if (!c.InBounds(map)) continue;
                                var thing = map.thingGrid.ThingAt(c, ThingCategory.Building);
                                if (thing != null && (thing.def.mineable || (thing.def.building != null && thing.def.building.isNaturalRock)))
                                {
                                    if (AddDesignationSafe(map, c, DesignationDefOf.Mine)) designated++;
                                }
                            }
                        }
                        return (designated > 0, $"mine {designated} cell(s) near {mineTarget.def.defName}");
                    }

                    case "grow":
                    {
                        string plant = NormalizePlantDef(cmd.Target);
                        var zone = CreateGrowingZoneSafe(map, CellFinder.StandableCellNear(safeBase, map, 10), 6, 6, plant);
                        return (zone != null, zone != null ? $"growing zone {zone.cells.Count} cells ({plant})" : "no fertile cells");
                    }

                    case "build":
                    {
                        var (defName, stuff) = NormalizeBuildTarget(cmd.Target);
                        var bp = TryPlaceBlueprint(map, defName, Offset(3, 4), Rot4.North, stuff);
                        return (bp != null, bp != null ? $"blueprint {defName} at {bp.Position}" : $"cannot place {defName}");
                    }

                    case "harvest_trees":
                    {
                        int designated = 0;
                        var trees = map.listerThings.AllThings
                            .Where(t => t.Spawned && t.def?.plant != null && t.def.plant.IsTree)
                            .OrderBy(t => t.Position.DistanceToSquared(safeBase))
                            .Take(15)
                            .ToList();
                        foreach (var tree in trees)
                        {
                            var desDef = (tree is Plant p && p.HarvestableNow) ? DesignationDefOf.HarvestPlant : DesignationDefOf.CutPlant;
                            if (AddDesignationSafe(map, tree.Position, desDef ?? DesignationDefOf.CutPlant)) designated++;
                        }
                        return (designated > 0, $"designated {designated} tree(s)");
                    }

                    case "build_room":
                    {
                        int placed = PlaceRoomBlueprint(map, safeBase, 7, 6);
                        return (placed > 0, placed > 0 ? $"room shell: {placed} wall/door blueprint(s)" : "no space for a 7x6 room");
                    }

                    case "hunt":
                    {
                        var animals = map.mapPawns.AllPawnsSpawned
                            .Where(a => a != null && !a.Dead && a.Faction == null && a.RaceProps != null && a.RaceProps.Animal
                                        && !a.RaceProps.predator && !a.InAggroMentalState
                                        && a.RaceProps.manhunterOnDamageChance <= 0.1f
                                        && a.RaceProps.baseBodySize <= 1.5f
                                        && !a.Position.Fogged(map)
                                        && map.designationManager.DesignationOn(a, DesignationDefOf.Hunt) == null)
                            .OrderBy(a => a.Position.DistanceToSquared(safeBase))
                            .Take(5)
                            .ToList();
                        foreach (var a in animals)
                        {
                            map.designationManager.AddDesignation(new Designation(a, DesignationDefOf.Hunt));
                        }
                        return (animals.Count > 0, $"hunt designated on {animals.Count} animal(s)");
                    }

                    case "stockpile":
                    {
                        var zone = CreateStockpileZoneSafe(map, CellFinder.StandableCellNear(safeBase, map, 6), 5, 5);
                        return (zone != null, zone != null ? $"stockpile {zone.cells.Count} cells" : "no free cells");
                    }

                    default:
                        return (false, $"unsupported action '{cmd.Action}'");
                }
            }
            catch (Exception ex)
            {
                return (false, $"exception: {ex.Message}");
            }
        }

        internal static List<PlayerViewColonist> BuildColonistVitals(Map? map, IEnumerable<PlaythroughColonistSnapshot> baseline)
        {
            var result = new List<PlayerViewColonist>();
            if (map == null) return result;

            var baselineByName = baseline?
                .Where(b => b != null && !string.IsNullOrEmpty(b.Name))
                .GroupBy(b => b.Name)
                .ToDictionary(g => g.Key, g => g.First()) ?? new Dictionary<string, PlaythroughColonistSnapshot>();

            foreach (var p in RimMindPawnLookup.GetSpawnedColonistsSafe(map))
            {
                if (p == null || p.Dead) continue;
                string name = p.Name?.ToStringShort ?? p.LabelShort;

                var bad = new List<string>();
                try
                {
                    if (p.needs?.mood?.thoughts != null)
                    {
                        var thoughts = new List<Thought>();
                        p.needs.mood.thoughts.GetAllMoodThoughts(thoughts);
                        foreach (var th in thoughts.Where(t => t != null && t.MoodOffset() < 0f).OrderBy(t => t.MoodOffset()).Take(5))
                        {
                            bad.Add($"{th.LabelCap} ({th.MoodOffset():F0})");
                        }
                    }
                }
                catch { }

                baselineByName.TryGetValue(name, out var snap);
                result.Add(new PlayerViewColonist
                {
                    Name = name,
                    Mood = p.needs?.mood?.CurLevel ?? 0f,
                    Food = p.needs?.food?.CurLevel ?? 0f,
                    Rest = p.needs?.rest?.CurLevel ?? 0f,
                    HealthSummary = p.health?.summaryHealth != null ? p.health.summaryHealth.SummaryHealthPercent.ToString("P0") : "?",
                    IsDowned = p.Downed,
                    BleedingRate = p.health?.hediffSet?.BleedRateTotal ?? 0f,
                    HasTendableHediff = p.health?.hediffSet?.HasTendableHediff() ?? false,
                    MentalState = p.MentalStateDef?.defName ?? string.Empty,
                    CurrentJob = p.CurJobDef?.defName ?? "Idle",
                    IsAgentActive = snap?.IsAgentActive ?? false,
                    AgentMode = snap?.AgentMode ?? string.Empty,
                    BadThoughts = bad
                });
            }
            return result;
        }

        internal static List<string> CollectActiveAlerts()
        {
            var labels = new List<string>();
            try
            {
                var readout = (Find.UIRoot as UIRoot_Play)?.alerts;
                var field = typeof(AlertsReadout).GetField("activeAlerts", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (readout != null && field?.GetValue(readout) is List<Alert> list)
                {
                    foreach (var a in list)
                    {
                        string label = a?.GetLabel() ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(label)) labels.Add(label);
                    }
                }
            }
            catch { }
            return labels;
        }

        internal static List<string> CollectOpenWindows()
        {
            var names = new List<string>();
            try
            {
                if (Find.WindowStack != null)
                {
                    foreach (var w in Find.WindowStack.Windows)
                    {
                        if (w != null) names.Add(w.GetType().Name);
                    }
                }
            }
            catch { }
            return names;
        }

        internal static int CountMedicine(Map? map)
        {
            int count = 0;
            var meds = map?.listerThings?.ThingsInGroup(ThingRequestGroup.Medicine);
            if (meds == null) return 0;
            foreach (var t in meds)
            {
                if (t != null && t.Spawned && !t.IsForbidden(Faction.OfPlayer)) count += t.stackCount;
            }
            return count;
        }

        internal static float CountUnstockpiledNutrition(Map? map)
        {
            float total = 0f;
            var foods = map?.listerThings?.ThingsInGroup(ThingRequestGroup.FoodSourceNotPlantOrTree);
            if (foods == null) return 0f;
            foreach (var t in foods)
            {
                if (t == null || !t.Spawned || t.def?.ingestible == null || t is Pawn || t is Corpse) continue;
                if (!t.def.IsNutritionGivingIngestible || !t.def.ingestible.HumanEdible) continue;
                if (t.IsForbidden(Faction.OfPlayer) || t.IsInValidStorage()) continue;
                total += t.GetStatValue(StatDefOf.Nutrition) * t.stackCount;
            }
            return total;
        }

        /// <summary>Places wall blueprints for a rectangular room shell with one door (a real player's drag-build).</summary>
        private static int PlaceRoomBlueprint(Map map, IntVec3 anchor, int width, int height)
        {
            var wallDef = DefDatabase<ThingDef>.GetNamedSilentFail("Wall");
            var doorDef = DefDatabase<ThingDef>.GetNamedSilentFail("Door");
            if (wallDef == null || doorDef == null) return 0;
            ThingDef stuff = ThingDefOf.WoodLog;

            foreach (var origin in GenRadial.RadialCellsAround(anchor, 25, true))
            {
                if (!origin.InBounds(map)) continue;
                var cells = new List<IntVec3>();
                for (int x = 0; x < width; x++)
                {
                    cells.Add(new IntVec3(origin.x + x, 0, origin.z));
                    cells.Add(new IntVec3(origin.x + x, 0, origin.z + height - 1));
                }
                for (int z = 1; z < height - 1; z++)
                {
                    cells.Add(new IntVec3(origin.x, 0, origin.z + z));
                    cells.Add(new IntVec3(origin.x + width - 1, 0, origin.z + z));
                }

                bool interiorFree = true;
                for (int x = 0; x < width && interiorFree; x++)
                {
                    for (int z = 0; z < height && interiorFree; z++)
                    {
                        var c = new IntVec3(origin.x + x, 0, origin.z + z);
                        if (!c.InBounds(map) || c.Fogged(map) || !c.Standable(map) || c.GetEdifice(map) != null)
                            interiorFree = false;
                    }
                }
                if (!interiorFree) continue;

                IntVec3 doorCell = new IntVec3(origin.x + width / 2, 0, origin.z);
                bool allOk = cells.All(c =>
                    GenConstruct.CanPlaceBlueprintAt(c == doorCell ? doorDef : wallDef, c, Rot4.North, map, false, null, null, stuff).Accepted);
                if (!allOk) continue;

                int placed = 0;
                foreach (var c in cells)
                {
                    GenConstruct.PlaceBlueprintForBuild(c == doorCell ? doorDef : wallDef, c, map, Rot4.North, Faction.OfPlayer, stuff);
                    placed++;
                }
                return placed;
            }
            return 0;
        }

        private static IntVec3 ResolveBaseCell(Map map)
        {
            var colonists = RimMindPawnLookup.GetSpawnedColonistsSafe(map)
                .Where(p => p != null && RimMindPawnLookup.IsActiveSpawnedColonist(p))
                .ToList();

            IntVec3 anchor = map.Center;
            if (colonists.Count > 0)
            {
                int x = (int)colonists.Average(p => p.Position.x);
                int z = (int)colonists.Average(p => p.Position.z);
                var c = new IntVec3(x, 0, z);
                if (c.InBounds(map)) anchor = c;
            }

            IntVec3 standable = CellFinder.StandableCellNear(anchor, map, 8);
            return standable.IsValid && standable.InBounds(map) ? standable : map.Center;
        }

        private static string NormalizePlantDef(string? target)
        {
            string t = (target ?? string.Empty).Trim();
            if (t.Length == 0 || t.IndexOf("rice", StringComparison.OrdinalIgnoreCase) >= 0) return "Plant_Rice";
            if (t.IndexOf("potato", StringComparison.OrdinalIgnoreCase) >= 0) return "Plant_Potato";
            if (t.IndexOf("corn", StringComparison.OrdinalIgnoreCase) >= 0) return "Plant_Corn";
            if (t.IndexOf("healroot", StringComparison.OrdinalIgnoreCase) >= 0) return "Plant_Healroot";
            if (t.IndexOf("cotton", StringComparison.OrdinalIgnoreCase) >= 0) return "Plant_Cotton";
            return t.StartsWith("Plant_", StringComparison.OrdinalIgnoreCase) ? t : "Plant_" + t;
        }

        private static (string DefName, string? Stuff) NormalizeBuildTarget(string? target)
        {
            string t = (target ?? string.Empty).Trim();
            string lower = t.ToLowerInvariant();
            if (lower.Length == 0 || lower.Contains("bed")) return ("Bed", "WoodLog");
            if (lower.Contains("stove")) return ("FueledStove", null);
            if (lower.Contains("butcher")) return ("TableButcher", "WoodLog");
            if (lower.Contains("passivecooler") || lower.Contains("passive_cooler")) return ("PassiveCooler", null);
            if (lower.Contains("cooler")) return ("Cooler", null);
            if (lower.Contains("heater")) return ("Heater", null);
            if (lower.Contains("solar")) return ("SolarGenerator", null);
            if (lower.Contains("battery")) return ("Battery", null);
            if (lower.Contains("torch")) return ("TorchLamp", null);
            if (lower.Contains("campfire")) return ("Campfire", null);
            if (lower.Contains("table")) return ("Table2x2c", "WoodLog");
            if (lower.Contains("stool")) return ("Stool", "WoodLog");
            if (lower.Contains("grave")) return ("Grave", null);
            return (t, null);
        }

        private static Blueprint? TryPlaceBlueprint(Map map, string defName, IntVec3 cell, Rot4 rot, string? stuffDefName)
        {
            BuildableDef? buildableDef = (BuildableDef?)DefDatabase<ThingDef>.GetNamedSilentFail(defName)
                                         ?? DefDatabase<TerrainDef>.GetNamedSilentFail(defName);
            if (buildableDef == null) return null;

            ThingDef? stuffDef = null;
            if (buildableDef is ThingDef thingDef && thingDef.MadeFromStuff)
            {
                if (!string.IsNullOrEmpty(stuffDefName))
                    stuffDef = DefDatabase<ThingDef>.GetNamedSilentFail(stuffDefName);
                stuffDef ??= GenStuff.DefaultStuffFor(thingDef) ?? ThingDefOf.WoodLog;
            }

            try
            {
                Rot4[] rots = (buildableDef is ThingDef td && td.rotatable)
                    ? new[] { rot, Rot4.North, Rot4.East, Rot4.South, Rot4.West }
                    : new[] { rot };

                foreach (var c in GenRadial.RadialCellsAround(cell.InBounds(map) ? cell : map.Center, 20, true))
                {
                    if (!c.InBounds(map) || c.Fogged(map) || !c.Standable(map)) continue;
                    foreach (var r in rots)
                    {
                        if (GenConstruct.CanPlaceBlueprintAt(buildableDef, c, r, map, false, null, null, stuffDef).Accepted)
                        {
                            return GenConstruct.PlaceBlueprintForBuild(buildableDef, c, map, r, Faction.OfPlayer, stuffDef);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"{LogTag} PlaceBlueprint {defName} notice: {ex.Message}");
            }
            return null;
        }

        private static bool AddDesignationSafe(Map map, IntVec3 cell, DesignationDef? desDef)
        {
            if (map == null || desDef == null || !cell.InBounds(map)) return false;
            try
            {
                if (desDef.targetType == TargetType.Thing)
                {
                    Thing? targetThing = (desDef == DesignationDefOf.HarvestPlant || desDef == DesignationDefOf.CutPlant)
                        ? cell.GetPlant(map)
                        : cell.GetFirstThing<Thing>(map);
                    if (targetThing != null && map.designationManager.DesignationOn(targetThing, desDef) == null)
                    {
                        map.designationManager.AddDesignation(new Designation(targetThing, desDef));
                        return true;
                    }
                    return false;
                }

                if (map.designationManager.DesignationAt(cell, desDef) == null)
                {
                    map.designationManager.AddDesignation(new Designation(cell, desDef));
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"{LogTag} AddDesignation {desDef.defName} notice: {ex.Message}");
            }
            return false;
        }

        private static List<IntVec3> CollectFreeCells(Map map, IntVec3 origin, int width, int height, bool requireFertility)
        {
            var cells = new List<IntVec3>(width * height);
            for (int x = 0; x < width; x++)
            {
                for (int z = 0; z < height; z++)
                {
                    var c = new IntVec3(origin.x + x, origin.y, origin.z + z);
                    if (!c.InBounds(map) || c.Fogged(map) || !c.Standable(map)) continue;
                    if (map.zoneManager.ZoneAt(c) != null) continue;
                    if (c.GetEdifice(map) != null) continue;
                    if (requireFertility && map.fertilityGrid != null && map.fertilityGrid.FertilityAt(c) < 0.7f) continue;
                    cells.Add(c);
                }
            }
            return cells;
        }

        private static List<IntVec3> FindBestCells(Map map, IntVec3 start, int width, int height, bool requireFertility, int[] offsets)
        {
            var best = CollectFreeCells(map, start, width, height, requireFertility);
            int target = (width * height) / 2;
            if (best.Count >= target) return best;

            foreach (int dx in offsets)
            {
                foreach (int dz in offsets)
                {
                    var candidate = CollectFreeCells(map, new IntVec3(start.x + dx, start.y, start.z + dz), width, height, requireFertility);
                    if (candidate.Count > best.Count) best = candidate;
                    if (best.Count >= target) return best;
                }
            }
            return best;
        }

        private static Zone_Growing? CreateGrowingZoneSafe(Map map, IntVec3 start, int width, int height, string plantDefName)
        {
            try
            {
                var cells = FindBestCells(map, start, width, height, true, new[] { -16, -10, 10, 16, -22, 22 });
                if (cells.Count == 0) return null;

                var zone = new Zone_Growing(map.zoneManager);
                map.zoneManager.RegisterZone(zone);
                foreach (var c in cells) zone.AddCell(c);
                var plantDef = DefDatabase<ThingDef>.GetNamedSilentFail(plantDefName);
                if (plantDef != null) zone.SetPlantDefToGrow(plantDef);
                return zone;
            }
            catch (Exception ex)
            {
                Log.Warning($"{LogTag} CreateGrowingZone notice: {ex.Message}");
                return null;
            }
        }

        private static Zone_Stockpile? CreateStockpileZoneSafe(Map map, IntVec3 start, int width, int height)
        {
            try
            {
                var cells = FindBestCells(map, start, width, height, false, new[] { -8, 8, -14, 14, -20, 20 });
                if (cells.Count == 0) return null;

                var zone = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, map.zoneManager);
                map.zoneManager.RegisterZone(zone);
                foreach (var c in cells) zone.AddCell(c);
                return zone;
            }
            catch (Exception ex)
            {
                Log.Warning($"{LogTag} CreateStockpileZone notice: {ex.Message}");
                return null;
            }
        }

        private static Thing? FindNearestMineable(Map map, IntVec3 center, string? specificDefName)
        {
            return map.listerThings.AllThings
                .Where(t => t.Spawned && t.def != null &&
                            (specificDefName != null
                                ? (t.def.defName.IndexOf(specificDefName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                   (t.def.building?.mineableThing != null &&
                                    t.def.building.mineableThing.defName.IndexOf(specificDefName, StringComparison.OrdinalIgnoreCase) >= 0))
                                : (t.def.mineable || (t.def.building != null && t.def.building.isNaturalRock))))
                .OrderBy(t => t.Position.DistanceToSquared(center))
                .FirstOrDefault();
        }
    }
}
