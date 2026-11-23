using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using RimMind.Application.Common.Constants;
using RimMind.Application.Common.Interfaces.Extension;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Models;
using RimMind.Application.Common.Models.Context;
using RimMind.Application.Common.Models.Prompt;
using RimMind.Application.Features.Context;
using RimMind.Application.Features.Prompt;
using RimMind.Domain.ValueObjects;
using RimMind.Presentation.Agent;
using RimMind.Presentation.Api;
using RimMind.Presentation.Settings;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimMind.Presentation.Agent
{
    /// <summary>
    /// Responsible for building Map-related context strings.
    /// Extracted from GameContextBuilder to satisfy SRP.
    /// </summary>
    public sealed class MapContextBuilder
    {
        private const long TicksPerDay = 60000L;
        private const int CacheStalenessTicks = 60;
        private const long WarningCooldownSeconds = 30L;

        private static readonly ConcurrentDictionary<(int settingsHash, int mapId, bool brief), (int tick, List<ContextEntry> entries)> _snapshotCache =
            new ConcurrentDictionary<(int settingsHash, int mapId, bool brief), (int tick, List<ContextEntry> entries)>();

        private static readonly ConcurrentDictionary<(int mapId, bool brief), List<ContextEntry>> _lastMainThreadSnapshot =
            new ConcurrentDictionary<(int mapId, bool brief), List<ContextEntry>>();

        private static readonly ConcurrentDictionary<string, long> _lastWarningTimestamp =
            new ConcurrentDictionary<string, long>();

        private readonly IContextSettings _contextSettings;

        public MapContextBuilder(IContextSettings? contextSettings = null)
        {
            _contextSettings = contextSettings ?? new ContextSettings();
        }

        /// <summary>
        /// Clears all static map snapshot caches (invoked on game start/stop via <see cref="IGameScopedService"/>).
        /// </summary>
        public static void ClearCaches()
        {
            _snapshotCache.Clear();
            _lastMainThreadSnapshot.Clear();
            _lastWarningTimestamp.Clear();
        }

        private IMapIncludeSettings MapSettings => _contextSettings;

        private IColonyIncludeSettings ColonySettings => _contextSettings;

        private IContextEnvironmentSettings EnvSettings => _contextSettings;

        private static void WarnRateLimited(string area, Exception ex)
        {
            try
            {
                long now = Stopwatch.GetTimestamp();
                long cooldown = WarningCooldownSeconds * Stopwatch.Frequency;
                if (_lastWarningTimestamp.TryGetValue(area, out long last) && now - last < cooldown)
                    return;
                _lastWarningTimestamp[area] = now;
                RimMindErrors.Warn($"[MapContextBuilder] {area} failed: {ex.Message}");
            }
            catch
            {
                // Ignore logging failures when Verse logger is unavailable in tests.
            }
        }

        /// <summary>
        /// Builds strictly static / semi-static map topology suitable for L1_Baseline (KV-Cache prefix-stable).
        /// Contains ONLY biome/size, colony room layout, and static worktable roster — NO time, season, weather,
        /// wealth, food storage, or volatile resource counts.
        /// </summary>
        public string BuildStaticMapTopology(Map? map)
        {
            if (map == null || !UnityData.IsInMainThread)
                return string.Empty;

            var sb = new StringBuilder();
            try
            {
                string biomeLabel = map.Biome?.label ?? map.Biome?.defName ?? "Unknown";
                sb.AppendLine($"Map Topology: Biome={biomeLabel}, Size={map.Size.x}x{map.Size.z}");
            }
            catch (Exception ex) { WarnRateLimited("StaticTopologyHeader", ex); }

            if (map.listerBuildings != null)
            {
                AppendColonyRoomsSummary(map, sb);
                try
                {
                    var buildings = map.listerBuildings.allBuildingsColonist;
                    if (buildings != null && buildings.Count > 0)
                    {
                        var worktables = new List<string>();
                        for (int i = 0; i < buildings.Count; i++)
                        {
                            var b = buildings[i];
                            if (b is Building_WorkTable wt && wt.def != null && !wt.Destroyed)
                            {
                                var recipes = wt.def.AllRecipes?
                                    .Take(3)
                                    .Select(r => r.defName)
                                    .ToList();
                                string recipeStr = (recipes != null && recipes.Count > 0) ? string.Join("/", recipes) : "None";
                                string tableName = wt.def.label ?? wt.def.defName;
                                worktables.Add($"{tableName}(id={wt.thingIDNumber}, recipes=[{recipeStr}])");
                                if (worktables.Count >= 4) break;
                            }
                        }

                        if (worktables.Count > 0)
                        {
                            sb.AppendLine($"WorktableRoster: [{string.Join("; ", worktables)}]");
                        }
                    }
                }
                catch (Exception ex) { WarnRateLimited("StaticWorktableRoster", ex); }
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Builds volatile colony resource and operational status suitable for L2_Environment.
        /// Keeps dynamic wealth, food, key resources, pending construction, worktable bill/fuel status,
        /// and stored apparel out of L1_Baseline so KV-Cache prefixes remain stable.
        /// </summary>
        public string BuildVolatileColonyResources(Map? map)
        {
            if (map == null || !UnityData.IsInMainThread)
                return string.Empty;

            var colony = ColonySettings;
            var sb = new StringBuilder();

            if (colony.IncludeWealth)
            {
                try
                {
                    float wealth = map.wealthWatcher?.WealthTotal ?? 0f;
                    string threat = ThreatLabel(wealth);
                    sb.AppendLine(RimMindText.Tr("RimMind.Prompt.WealthWithThreat", $"{wealth:F0}", threat));
                }
                catch (Exception ex) { WarnRateLimited("VolatileWealth", ex); }
            }

            if (colony.IncludeFood)
            {
                float foodNutrition = MapResourceCounter.GetAvailableFoodNutrition(map);
                sb.AppendLine(RimMindText.Tr("RimMind.Prompt.FoodStorage", $"{foodNutrition:F0}"));

                int wood = MapResourceCounter.GetAvailableCount(map, ThingDefOf.WoodLog);
                int steel = MapResourceCounter.GetAvailableCount(map, ThingDefOf.Steel);
                int med = CountAvailableMedicine(map);
                sb.AppendLine(RimMindText.TrOr(
                    "RimMind.Prompt.KeyResources",
                    "Key Resources: Wood {0}, Steel {1}, Medicine {2}",
                    wood, steel, med));

                int totalPending = CountPendingConstruction(map);
                if (totalPending > 0)
                {
                    sb.AppendLine(RimMindText.TrOr(
                        "RimMind.Prompt.PendingConstruction",
                        "Pending Construction: {0} blueprints/frames",
                        totalPending));
                }

                AppendWorktableStatus(map, sb);
                AppendStoredApparelSummary(map, sb);
            }

            return sb.ToString().TrimEnd();
        }

        public string BuildMapContext(Map map, bool brief = false)
        {
            var entries = BuildMapContextEntries(map, brief);
            var sb = new StringBuilder();
            foreach (var entry in entries)
            {
                if (!string.IsNullOrEmpty(entry.Content))
                    sb.AppendLine(entry.Content);
            }
            return sb.ToString().TrimEnd();
        }

        public List<ContextEntry> BuildMapContextEntries(Map map, bool brief = false)
        {
            var entries = new List<ContextEntry>();
            if (map == null) return entries;

            int mapId = map.uniqueID;
            if (!UnityData.IsInMainThread)
            {
                WarnRateLimited("OffMainThread", new InvalidOperationException("BuildMapContextEntries called off main thread; returning cached snapshot or empty."));
                if (_lastMainThreadSnapshot.TryGetValue((mapId, brief), out var fallbackSnapshot))
                    return new List<ContextEntry>(fallbackSnapshot);
                if (brief && _lastMainThreadSnapshot.TryGetValue((mapId, false), out var fullSnapshot))
                    return new List<ContextEntry>(fullSnapshot);
                return entries;
            }

            int currentTick = -1;
            try { currentTick = Find.TickManager?.TicksGame ?? -1; }
            catch (Exception ex) { WarnRateLimited("TickManager", ex); }

            int settingsHash = RuntimeHelpers.GetHashCode(_contextSettings);
            var cacheKey = (settingsHash, mapId, brief);
            if (currentTick >= 0
                && _snapshotCache.TryGetValue(cacheKey, out var cached)
                && currentTick >= cached.tick
                && currentTick - cached.tick <= CacheStalenessTicks)
            {
                return new List<ContextEntry>(cached.entries);
            }

            var ctx = MapSettings;
            var colony = ColonySettings;

            entries.Add(new ContextEntry(RimMindText.Tr("RimMind.Prompt.MapStatusHeader")));

            if (ctx.IncludeGameTime && Find.TickManager != null && Find.WorldGrid != null)
            {
                long ticks = Find.TickManager.TicksAbs;
                Vector2 longLat = Find.WorldGrid.LongLatOf(map.Tile);
                int hour = GenDate.HourOfDay(ticks, longLat.x);
                string dateStr = GenDate.DateFullStringAt(ticks, longLat);
                int day = (int)(ticks / TicksPerDay);
                entries.Add(new ContextEntry(
                    RimMindText.Tr("RimMind.Prompt.TimeFormat", dateStr, $"{hour:D2}"))
                {
                    Metadata = new Dictionary<string, string>
                    {
                        ["key"] = "time",
                        ["hour"] = hour.ToString(),
                        ["day"] = day.ToString()
                    }
                });
            }

            if (colony.IncludeColonistCount)
            {
                var colonists = RimMindPawnLookup.GetSpawnedColonistsSafe(map);
                int count = colonists.Count;
                string content;
                if (!brief && colony.IncludeColonistNames && colonists.Count > 0)
                {
                    var names = colonists.Select(p => p.Name.ToStringShort);
                    string nameList = string.Join(", ", names);
                    content = RimMindText.Tr("RimMind.Prompt.ColonistCount", count, nameList);
                }
                else
                {
                    content = RimMindText.Tr("RimMind.Prompt.ColonistCountBrief", count);
                }
                entries.Add(new ContextEntry(content)
                {
                    Metadata = new Dictionary<string, string>
                    {
                        ["key"] = "colonistCount",
                        ["count"] = count.ToString()
                    }
                });
            }

            var otherSb = new StringBuilder();

            if (!brief && colony.IncludeColonistNames)
            {
                var prisoners = RimMindPawnLookup.GetSpawnedPrisonersSafe(map);
                if (prisoners.Count > 0)
                {
                    var names = prisoners.Select(p => p.Name.ToStringShort);
                    string nameList = string.Join(", ", names);
                    otherSb.AppendLine(RimMindText.Tr("RimMind.Prompt.PrisonerCount", prisoners.Count, nameList));
                }
            }

            if (colony.IncludeWealth)
            {
                try
                {
                    float wealth = map.wealthWatcher?.WealthTotal ?? 0f;
                    string threat = ThreatLabel(wealth);
                    otherSb.AppendLine(RimMindText.Tr("RimMind.Prompt.WealthWithThreat", $"{wealth:F0}", threat));
                }
                catch (Exception ex) { WarnRateLimited("Wealth", ex); }
            }

            if (colony.IncludeThreats)
            {
                try
                {
                    float wealth = map.wealthWatcher?.WealthTotal ?? 0f;
                    string threat = ThreatLabel(wealth);
                    otherSb.AppendLine(RimMindText.Tr("RimMind.Prompt.ThreatLevel", threat));
                }
                catch (Exception ex) { WarnRateLimited("Threats", ex); }
            }

            if (colony.IncludeFood)
            {
                float foodNutrition = MapResourceCounter.GetAvailableFoodNutrition(map);
                otherSb.AppendLine(RimMindText.Tr("RimMind.Prompt.FoodStorage", $"{foodNutrition:F0}"));

                int wood = MapResourceCounter.GetAvailableCount(map, ThingDefOf.WoodLog);
                int steel = MapResourceCounter.GetAvailableCount(map, ThingDefOf.Steel);
                int med = CountAvailableMedicine(map);
                otherSb.AppendLine(RimMindText.TrOr(
                    "RimMind.Prompt.KeyResources",
                    "Key Resources: Wood {0}, Steel {1}, Medicine {2}",
                    wood, steel, med));

                int totalPending = CountPendingConstruction(map);
                if (totalPending > 0)
                {
                    otherSb.AppendLine(RimMindText.TrOr(
                        "RimMind.Prompt.PendingConstruction",
                        "Pending Construction: {0} blueprints/frames",
                        totalPending));
                }

                if (!brief && map.listerBuildings != null)
                {
                    AppendColonyRoomsSummary(map, otherSb);
                    AppendWorktableStatus(map, otherSb);
                }

                if (!brief)
                {
                    AppendStoredApparelSummary(map, otherSb);
                }
            }

            if (ctx.IncludeSeason)
            {
                string seasonStr = GenLocalDate.Season(map).Label();
                otherSb.AppendLine(RimMindText.Tr("RimMind.Prompt.Season", seasonStr));
            }
            if (ctx.IncludeWeather)
            {
                otherSb.AppendLine(RimMindText.Tr("RimMind.Prompt.Weather", map.weatherManager.curWeather.label));
            }

            string otherContent = otherSb.ToString().TrimEnd();
            if (!string.IsNullOrEmpty(otherContent))
                entries.Add(new ContextEntry(otherContent));

            var snapshotCopy = new List<ContextEntry>(entries);
            _lastMainThreadSnapshot[(mapId, brief)] = snapshotCopy;
            if (currentTick >= 0)
            {
                _snapshotCache[cacheKey] = (currentTick, snapshotCopy);
            }

            return entries;
        }

        public PromptSection BuildMapContextSection(Map map, bool brief = false)
        {
            string content = BuildMapContext(map, brief);
            var section = new PromptSection("map_context", content, PromptSection.PriorityKeyState);
            if (!brief)
            {
                section.Compress = raw => CompressRenderedMapContext(raw);
            }
            return section;
        }

        private static string CompressRenderedMapContext(string? raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            var lines = raw!.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            var sb = new StringBuilder();
            foreach (var line in lines)
            {
                if (line.StartsWith("Worktables:", StringComparison.Ordinal)
                    || line.StartsWith("AvailableApparel:", StringComparison.Ordinal))
                {
                    continue;
                }
                sb.AppendLine(line);
            }
            return sb.ToString().TrimEnd();
        }

        private string ThreatLabel(float wealth)
        {
            float high = EnvSettings?.ThreatThresholdHigh ?? RimMindDefaults.DefaultThreatThresholdHigh;
            float medium = EnvSettings?.ThreatThresholdMedium ?? RimMindDefaults.DefaultThreatThresholdMedium;
            float low = EnvSettings?.ThreatThresholdLow ?? RimMindDefaults.DefaultThreatThresholdLow;

            float threatScale = 1f;
            try { threatScale = Find.Storyteller?.difficulty?.threatScale ?? 1f; } catch (System.Exception) { /* Storyteller may be null during early init */ }
            if (threatScale <= 0f) threatScale = 1f;

            string tier = ThreatClassifier.ClassifyThreatTier(wealth, high, medium, low, threatScale);
            return tier switch
            {
                "Extreme" => RimMindText.Tr("RimMind.Prompt.Threat.Extreme"),
                "High"    => RimMindText.Tr("RimMind.Prompt.Threat.High"),
                "Medium"  => RimMindText.Tr("RimMind.Prompt.Threat.Medium"),
                _         => RimMindText.Tr("RimMind.Prompt.Threat.Low")
            };
        }

        private static int CountAvailableMedicine(Map map)
        {
            int med = 0;
            try
            {
                var medThings = map.listerThings?.ThingsInGroup(ThingRequestGroup.Medicine);
                if (medThings != null)
                {
                    for (int i = 0; i < medThings.Count; i++)
                    {
                        var t = medThings[i];
                        if (t != null && t.Spawned && !t.IsForbidden(Faction.OfPlayer))
                            med += t.stackCount;
                    }
                }
            }
            catch (Exception ex) { WarnRateLimited("Medicine", ex); }
            return med;
        }

        private static int CountPendingConstruction(Map map)
        {
            try
            {
                int pendingBlueprints = map.listerThings?.ThingsInGroup(ThingRequestGroup.Blueprint)?.Count ?? 0;
                int pendingFrames = map.listerThings?.ThingsInGroup(ThingRequestGroup.BuildingFrame)?.Count ?? 0;
                return pendingBlueprints + pendingFrames;
            }
            catch (Exception ex)
            {
                WarnRateLimited("PendingConstruction", ex);
                return 0;
            }
        }

        private static void AppendWorktableStatus(Map map, StringBuilder sb)
        {
            if (map.listerBuildings == null) return;

            var worktables = new List<string>();
            try
            {
                var buildings = map.listerBuildings.allBuildingsColonist;
                if (buildings != null)
                {
                    for (int i = 0; i < buildings.Count; i++)
                    {
                        var b = buildings[i];
                        if (b is Building_WorkTable wt && wt.def != null && !wt.Destroyed)
                        {
                            int billCount = wt.billStack?.Count ?? 0;
                            string activeBillsStr = "";
                            if (billCount > 0 && wt.billStack?.Bills != null)
                            {
                                var activeBillNames = wt.billStack.Bills
                                    .Where(bill => bill?.recipe != null && !bill.suspended)
                                    .Select(bill => bill.recipe.defName)
                                    .Distinct()
                                    .ToList();
                                if (activeBillNames.Count > 0)
                                    activeBillsStr = $"[{string.Join("/", activeBillNames)}]";
                            }
                            var recipes = wt.def.AllRecipes?
                                .Take(3)
                                .Select(r => r.defName)
                                .ToList();
                            string recipeStr = (recipes != null && recipes.Count > 0) ? string.Join("/", recipes) : "None";
                            string tableName = wt.def.label ?? wt.def.defName;
                            var blockReasons = new List<string>();
                            var refuelable = wt.GetComp<CompRefuelable>();
                            if (refuelable != null && !refuelable.HasFuel)
                            {
                                blockReasons.Add("0 WoodLog fuel — harvest trees/haul wood instead of adding bills");
                            }
                            var powerComp = wt.GetComp<CompPowerTrader>();
                            if (powerComp != null && !powerComp.PowerOn)
                            {
                                blockReasons.Add("unpowered");
                            }
                            if (billCount > 0 && (wt.def.defName.Contains("Stove") || wt.def.defName.Contains("Campfire")))
                            {
                                var rawCat = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("FoodRaw");
                                var rawNutrition = (rawCat != null && map.resourceCounter != null) ? map.resourceCounter.GetCountIn(rawCat) : 0;
                                if (rawNutrition <= 0 && map.listerThings != null)
                                {
                                    var foods = map.listerThings.ThingsInGroup(ThingRequestGroup.FoodSourceNotPlantOrTree);
                                    if (foods != null)
                                    {
                                        for (int f = 0; f < foods.Count; f++)
                                        {
                                            var ft = foods[f];
                                            if (ft != null && ft.Spawned && !ft.IsForbidden(Faction.OfPlayer)
                                                && ft.def?.thingCategories != null && rawCat != null
                                                && ft.def.thingCategories.Any(c => c == rawCat || (c.Parents != null && c.Parents.Contains(rawCat))))
                                            {
                                                rawNutrition += ft.stackCount;
                                            }
                                        }
                                    }
                                }
                                if (rawNutrition <= 0)
                                {
                                    blockReasons.Add("0 raw food available, harvest/hunt needed");
                                }
                            }
                            string blockNotice = blockReasons.Count > 0
                                ? $" [BLOCKED: {string.Join("; ", blockReasons)}]"
                                : "";
                            worktables.Add($"{tableName}(id={wt.thingIDNumber}, bills={billCount}{activeBillsStr}{blockNotice}, recipes=[{recipeStr}])");
                            if (worktables.Count >= 4) break;
                        }
                    }
                }
            }
            catch (Exception ex) { WarnRateLimited("Worktables", ex); }

            if (worktables.Count > 0)
            {
                string tableList = string.Join("; ", worktables);
                sb.AppendLine(RimMindText.TrOr(
                    "RimMind.Prompt.ColonyWorktables",
                    "Worktables: [{0}]",
                    tableList));
            }
        }

        private static void AppendStoredApparelSummary(Map map, StringBuilder sb)
        {
            try
            {
                var storageApparel = map.listerThings?.ThingsInGroup(ThingRequestGroup.Apparel);
                if (storageApparel == null || storageApparel.Count == 0)
                    return;

                var apparelEntries = new List<string>();
                for (int i = 0; i < storageApparel.Count; i++)
                {
                    var a = storageApparel[i];
                    if (a != null && a.Spawned && !a.IsForbidden(Faction.OfPlayer) && a.IsInAnyStorage() && a.def != null)
                    {
                        string itemLabel = a.def.label ?? a.def.defName;
                        int hpPct = a.MaxHitPoints > 0 ? Mathf.RoundToInt((float)a.HitPoints / a.MaxHitPoints * 100f) : 100;
                        apparelEntries.Add($"{itemLabel}(id={a.thingIDNumber}, hp={hpPct}%)");
                        if (apparelEntries.Count >= 5) break;
                    }
                }

                if (apparelEntries.Count > 0)
                {
                    string apList = string.Join(", ", apparelEntries);
                    sb.AppendLine(RimMindText.TrOr(
                        "RimMind.Prompt.StoredApparel",
                        "AvailableApparel: [{0}]",
                        apList));
                }
            }
            catch (Exception ex) { WarnRateLimited("StoredApparel", ex); }
        }

        private static void AppendColonyRoomsSummary(Map map, StringBuilder sb)
        {
            if (map == null || sb == null) return;
            try
            {
                var buildings = map.listerBuildings?.allBuildingsColonist;
                if (buildings == null || buildings.Count == 0) return;

                var stats = new Dictionary<int, (int Cells, int Beds, int Work, int Tables, long SumX, long SumZ, int Samples)>();
                int limit = Math.Min(buildings.Count, 200);
                int wallProbes = 0;

                for (int i = 0; i < limit; i++)
                {
                    var b = buildings[i];
                    if (b == null || !b.Spawned || b.Destroyed) continue;

                    if (b.def != null && b.def.passability == Traversability.Impassable)
                    {
                        if (wallProbes < 48)
                        {
                            wallProbes++;
                            for (int d = 0; d < 4; d++)
                            {
                                IntVec3 nCell = b.Position + GenAdj.CardinalDirections[d];
                                if (!nCell.InBounds(map) || nCell.Fogged(map)) continue;
                                Room? nRoom = nCell.GetRoom(map);
                                if (nRoom == null || nRoom.PsychologicallyOutdoors || nRoom.TouchesMapEdge || nRoom.CellCount <= 0) continue;
                                if (!stats.TryGetValue(nRoom.ID, out var cur))
                                    cur = (nRoom.CellCount, 0, 0, 0, 0, 0, 0);
                                stats[nRoom.ID] = (cur.Cells, cur.Beds, cur.Work, cur.Tables, cur.SumX + nCell.x, cur.SumZ + nCell.z, cur.Samples + 1);
                            }
                        }
                        continue;
                    }

                    Room? room = b.Position.InBounds(map) ? b.Position.GetRoom(map) : null;
                    if (room == null || room.PsychologicallyOutdoors || room.TouchesMapEdge || room.CellCount <= 0) continue;

                    if (!stats.TryGetValue(room.ID, out var entry))
                        entry = (room.CellCount, 0, 0, 0, 0, 0, 0);

                    int addBeds = b is Building_Bed ? 1 : 0;
                    int addWork = b is Building_WorkTable ? 1 : 0;
                    int addTable = b.def?.surfaceType == SurfaceType.Eat ? 1 : 0;
                    stats[room.ID] = (
                        entry.Cells,
                        entry.Beds + addBeds,
                        entry.Work + addWork,
                        entry.Tables + addTable,
                        entry.SumX + b.Position.x,
                        entry.SumZ + b.Position.z,
                        entry.Samples + 1);
                }

                if (stats.Count == 0) return;

                var ordered = stats
                    .OrderByDescending(kv => kv.Value.Beds + kv.Value.Work + kv.Value.Tables)
                    .ThenByDescending(kv => kv.Value.Cells)
                    .Take(6)
                    .Select(kv =>
                    {
                        var v = kv.Value;
                        int cx = v.Samples > 0 ? (int)(v.SumX / v.Samples) : 0;
                        int cz = v.Samples > 0 ? (int)(v.SumZ / v.Samples) : 0;
                        string role = v.Beds > 1 ? "Barracks"
                            : v.Beds == 1 ? "Bedroom"
                            : v.Work > 0 ? "Workshop"
                            : v.Tables > 0 ? "Dining"
                            : "Indoor";
                        return $"{role}@({cx},{cz},cells={v.Cells},beds={v.Beds},work={v.Work})";
                    })
                    .ToList();

                string summaryList = string.Join("; ", ordered);
                sb.AppendLine(RimMindText.TrOr(
                    "RimMind.Prompt.ColonyRooms",
                    "ColonyRooms: [{0}]",
                    summaryList));
            }
            catch (Exception ex) { WarnRateLimited("AppendColonyRoomsSummary", ex); }
        }
    }

    internal sealed class MapContextBuilderCacheLifecycleService : IGameScopedService
    {
        public static readonly MapContextBuilderCacheLifecycleService Instance = new MapContextBuilderCacheLifecycleService();

        public string Id => "core.map_context_builder.cache_lifecycle";

        public string OwnerModId => RimMindOwnerConsts.CoreModId;

        public void OnGameStarted() => MapContextBuilder.ClearCaches();

        public void OnGameStopped() => MapContextBuilder.ClearCaches();
    }
}
