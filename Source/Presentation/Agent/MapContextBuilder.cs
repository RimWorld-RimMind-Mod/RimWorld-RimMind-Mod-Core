using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using RimMind.Application.Common.Interfaces.Internal;
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
        private const float ThreatThresholdHigh = 200000f;
        private const float ThreatThresholdMedium = 100000f;
        private const float ThreatThresholdLow = 50000f;

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

        private static string Tr(string key, params object[] args)
        {
            try
            {
                if (string.IsNullOrEmpty(key)) return string.Empty;
                string template = key.CanTranslate() ? key.TranslateSimple() : key;
                return (args == null || args.Length == 0) ? template : string.Format(template, args);
            }
            catch { return key ?? string.Empty; }
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

            entries.Add(new ContextEntry(Tr("RimMind.Prompt.MapStatusHeader")));

            if (ctx.IncludeGameTime)
            {
                long ticks = Find.TickManager.TicksAbs;
                Vector2 longLat = Find.WorldGrid.LongLatOf(map.Tile);
                int hour = GenDate.HourOfDay(ticks, longLat.x);
                string dateStr = GenDate.DateFullStringAt(ticks, longLat);
                int day = (int)(ticks / TicksPerDay);
                entries.Add(new ContextEntry(
                    Tr("RimMind.Prompt.TimeFormat", dateStr, $"{hour:D2}"))
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
                    content = Tr("RimMind.Prompt.ColonistCount", count, nameList);
                }
                else
                {
                    content = Tr("RimMind.Prompt.ColonistCountBrief", count);
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
                    otherSb.AppendLine(Tr("RimMind.Prompt.PrisonerCount", prisoners.Count, nameList));
                }
            }

            if (colony.IncludeWealth)
            {
                try
                {
                    float wealth = map.wealthWatcher?.WealthTotal ?? 0f;
                    string threat = ThreatLabel(wealth);
                    otherSb.AppendLine(Tr("RimMind.Prompt.WealthWithThreat", $"{wealth:F0}", threat));
                }
                catch (Exception ex) { WarnRateLimited("Wealth", ex); }
            }

            if (colony.IncludeThreats)
            {
                try
                {
                    float wealth = map.wealthWatcher?.WealthTotal ?? 0f;
                    string threat = ThreatLabel(wealth);
                    otherSb.AppendLine(Tr("RimMind.Prompt.ThreatLevel", threat));
                }
                catch (Exception ex) { WarnRateLimited("Threats", ex); }
            }

            if (colony.IncludeFood)
            {
                float foodNutrition = 0f;
                try
                {
                    foodNutrition = map.resourceCounter?.TotalHumanEdibleNutrition ?? 0f;
                }
                catch (Exception ex) { WarnRateLimited("FoodNutrition", ex); }
                otherSb.AppendLine(Tr("RimMind.Prompt.FoodStorage", $"{foodNutrition:F0}"));

                int wood = CountAvailableMapResource(map, ThingDefOf.WoodLog);
                int steel = CountAvailableMapResource(map, ThingDefOf.Steel);
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
                string resKey = "RimMind.Prompt.KeyResources";
                otherSb.AppendLine(resKey.CanTranslate()
                    ? Tr(resKey, wood, steel, med)
                    : $"Key Resources: Wood {wood}, Steel {steel}, Medicine {med}");

                int totalPending = 0;
                try
                {
                    int pendingBlueprints = map.listerThings?.ThingsInGroup(ThingRequestGroup.Blueprint)?.Count ?? 0;
                    int pendingFrames = map.listerThings?.ThingsInGroup(ThingRequestGroup.BuildingFrame)?.Count ?? 0;
                    totalPending = pendingBlueprints + pendingFrames;
                }
                catch (Exception ex) { WarnRateLimited("PendingConstruction", ex); }
                if (totalPending > 0)
                {
                    string pendingKey = "RimMind.Prompt.PendingConstruction";
                    otherSb.AppendLine(pendingKey.CanTranslate()
                        ? Tr(pendingKey, totalPending)
                        : $"Pending Construction: {totalPending} blueprints/frames (Shortage Notice: gather wood/minerals if materials are low)");
                }

                if (!brief && map.listerBuildings != null)
                {
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
                        string wtKey = "RimMind.Prompt.ColonyWorktables";
                        string tableList = string.Join("; ", worktables);
                        otherSb.AppendLine(wtKey.CanTranslate()
                            ? Tr(wtKey, tableList)
                            : $"Worktables: [{tableList}] (Queue bills via actions_add_worktable_bill)");
                    }
                }

                if (!brief)
                {
                    try
                    {
                        var storageApparel = map.listerThings?.ThingsInGroup(ThingRequestGroup.Apparel);
                        if (storageApparel != null && storageApparel.Count > 0)
                        {
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
                                string apKey = "RimMind.Prompt.StoredApparel";
                                string apList = string.Join(", ", apparelEntries);
                                otherSb.AppendLine(apKey.CanTranslate()
                                    ? Tr(apKey, apList)
                                    : $"AvailableApparel: [{apList}] (Equip protective/thermal gear via actions_manage_equipment)");
                            }
                        }
                    }
                    catch (Exception ex) { WarnRateLimited("StoredApparel", ex); }
                }
            }

            if (ctx.IncludeSeason)
            {
                string seasonStr = GenLocalDate.Season(map).Label();
                otherSb.Append(Tr("RimMind.Prompt.Season", seasonStr));
            }
            if (ctx.IncludeWeather)
                otherSb.AppendLine(Tr("RimMind.Prompt.Weather", map.weatherManager.curWeather.label));
            else if (ctx.IncludeSeason)
                otherSb.AppendLine();

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
            var lines = raw.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
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
            float high = EnvSettings?.ThreatThresholdHigh ?? ThreatThresholdHigh;
            float medium = EnvSettings?.ThreatThresholdMedium ?? ThreatThresholdMedium;
            float low = EnvSettings?.ThreatThresholdLow ?? ThreatThresholdLow;

            float threatScale = 1f;
            try { threatScale = Find.Storyteller?.difficulty?.threatScale ?? 1f; } catch (System.Exception) { /* Storyteller may be null during early init */ }
            if (threatScale <= 0f) threatScale = 1f;

            string tier = ThreatClassifier.ClassifyThreatTier(wealth, high, medium, low, threatScale);
            return tier switch
            {
                "Extreme" => Tr("RimMind.Prompt.Threat.Extreme"),
                "High"    => Tr("RimMind.Prompt.Threat.High"),
                "Medium"  => Tr("RimMind.Prompt.Threat.Medium"),
                _         => Tr("RimMind.Prompt.Threat.Low")
            };
        }

        private static int CountAvailableMapResource(Map? map, ThingDef? def)
        {
            if (map == null || def == null) return 0;
            int counted = 0;
            try
            {
                counted = map.resourceCounter?.GetCount(def) ?? 0;
                var spawnedList = map.listerThings?.ThingsOfDef(def);
                if (spawnedList != null && spawnedList.Count > 0)
                {
                    int unforbiddenTotal = 0;
                    for (int i = 0; i < spawnedList.Count; i++)
                    {
                        var t = spawnedList[i];
                        if (t != null && t.Spawned && !t.Destroyed && !t.Position.Fogged(map) && !t.IsForbidden(Faction.OfPlayer))
                        {
                            unforbiddenTotal += t.stackCount;
                        }
                    }
                    if (unforbiddenTotal > counted)
                        counted = unforbiddenTotal;
                }
            }
            catch (Exception ex) { WarnRateLimited("CountAvailableMapResource", ex); }
            return counted;
        }
    }
}
