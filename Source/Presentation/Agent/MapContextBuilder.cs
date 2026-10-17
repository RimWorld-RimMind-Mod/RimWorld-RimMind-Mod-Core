using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Models.Context;
using RimMind.Application.Common.Models.Prompt;
using RimMind.Application.Features.Context;
using RimMind.Application.Features.Prompt;
using RimMind.Domain.ValueObjects;
using RimMind.Presentation.Agent;
using RimMind.Presentation.Api;
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
        private const float ThreatThresholdHigh = 200000f;
        private const float ThreatThresholdMedium = 100000f;
        private const float ThreatThresholdLow = 50000f;

        private readonly IContextSettings? _contextSettings;

        public MapContextBuilder(IContextSettings? contextSettings = null)
        {
            _contextSettings = contextSettings;
        }

        private IMapIncludeSettings? MapSettings => _contextSettings;

        private IColonyIncludeSettings? ColonySettings => _contextSettings;

        private IContextEnvironmentSettings? EnvSettings => _contextSettings as IContextEnvironmentSettings;

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

            var ctx = MapSettings;
            var colony = ColonySettings;

            entries.Add(new ContextEntry(Tr("RimMind.Prompt.MapStatusHeader")));

            if (ctx.IncludeGameTime)
            {
                long ticks = Find.TickManager.TicksAbs;
                Vector2 longLat = Find.WorldGrid.LongLatOf(map.Tile);
                int hour = GenDate.HourOfDay(ticks, longLat.x);
                string dateStr = UnityData.IsInMainThread
                    ? GenDate.DateFullStringAt(ticks, longLat)
                    : $"{GenDate.DayOfQuadrum(ticks, longLat.x) + 1} {GenDate.Quadrum(ticks, longLat.x)}, {GenDate.Year(ticks, longLat.x)}";
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
                if (colony.IncludeColonistNames && colonists.Count > 0)
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

            if (colony.IncludeColonistNames)
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
                    float wealth = UnityData.IsInMainThread ? (map.wealthWatcher?.WealthTotal ?? 0f) : 0f;
                    string threat = ThreatLabel(wealth);
                    otherSb.AppendLine(Tr("RimMind.Prompt.WealthWithThreat", $"{wealth:F0}", threat));
                }
                catch { }
            }

            if (colony.IncludeThreats)
            {
                try
                {
                    float wealth = UnityData.IsInMainThread ? (map.wealthWatcher?.WealthTotal ?? 0f) : 0f;
                    string threat = ThreatLabel(wealth);
                    otherSb.AppendLine(Tr("RimMind.Prompt.ThreatLevel", threat));
                }
                catch { }
            }

            if (colony.IncludeFood)
            {
                float foodNutrition = 0f;
                try
                {
                    var allDefs = DefDatabase<ThingDef>.AllDefsListForReading;
                    for (int i = 0; i < allDefs.Count; i++)
                    {
                        var def = allDefs[i];
                        if (def.IsNutritionGivingIngestible && def.ingestible != null
                            && def.ingestible.HumanEdible && !def.IsCorpse)
                        {
                            foodNutrition += (map.resourceCounter?.GetCount(def) ?? 0) * def.ingestible.CachedNutrition;
                        }
                    }
                }
                catch { }
                otherSb.AppendLine(Tr("RimMind.Prompt.FoodStorage", $"{foodNutrition:F0}"));

                int wood = CountAvailableMapResource(map, ThingDefOf.WoodLog);
                int steel = CountAvailableMapResource(map, ThingDefOf.Steel);
                int med = 0;
                try
                {
                    var medThings = map.listerThings?.ThingsInGroup(ThingRequestGroup.Medicine)?.ToArray();
                    if (medThings != null)
                    {
                        for (int i = 0; i < medThings.Length; i++)
                        {
                            var t = medThings[i];
                            if (t != null && t.Spawned && !t.IsForbidden(Faction.OfPlayer))
                                med += t.stackCount;
                        }
                    }
                }
                catch { }
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
                catch { }
                if (totalPending > 0)
                {
                    string pendingKey = "RimMind.Prompt.PendingConstruction";
                    otherSb.AppendLine(pendingKey.CanTranslate()
                        ? Tr(pendingKey, totalPending)
                        : $"Pending Construction: {totalPending} blueprints/frames (Shortage Notice: gather wood/minerals if materials are low)");
                }

                if (map.listerBuildings != null)
                {
                    var worktables = new List<string>();
                    try
                    {
                        var buildings = map.listerBuildings.allBuildingsColonist?.ToArray();
                        if (buildings != null)
                        {
                            for (int i = 0; i < buildings.Length; i++)
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
                                            var foods = map.listerThings.ThingsInGroup(ThingRequestGroup.FoodSourceNotPlantOrTree)?.ToArray();
                                            if (foods != null)
                                            {
                                                for (int f = 0; f < foods.Length; f++)
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
                    catch { }
                    if (worktables.Count > 0)
                    {
                        string wtKey = "RimMind.Prompt.ColonyWorktables";
                        string tableList = string.Join("; ", worktables);
                        otherSb.AppendLine(wtKey.CanTranslate()
                            ? Tr(wtKey, tableList)
                            : $"Worktables: [{tableList}] (Queue bills via actions_add_worktable_bill)");
                    }
                }

                try
                {
                    var storageApparel = map.listerThings?.ThingsInGroup(ThingRequestGroup.Apparel)?.ToArray();
                    if (storageApparel != null && storageApparel.Length > 0)
                    {
                        var apparelEntries = new List<string>();
                        for (int i = 0; i < storageApparel.Length; i++)
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
                catch { }
            }

            if (ctx.IncludeSeason)
            {
                string seasonStr = UnityData.IsInMainThread ? GenLocalDate.Season(map).Label() : GenLocalDate.Season(map).ToString();
                otherSb.Append(Tr("RimMind.Prompt.Season", seasonStr));
            }
            if (ctx.IncludeWeather)
                otherSb.AppendLine(Tr("RimMind.Prompt.Weather", map.weatherManager.curWeather.label));
            else if (ctx.IncludeSeason)
                otherSb.AppendLine();

            string otherContent = otherSb.ToString().TrimEnd();
            if (!string.IsNullOrEmpty(otherContent))
                entries.Add(new ContextEntry(otherContent));

            return entries;
        }

        public PromptSection BuildMapContextSection(Map map, bool brief = false)
        {
            var section = new PromptSection("map_context", BuildMapContext(map, brief), PromptSection.PriorityKeyState);
            section.Compress = _ => BuildMapContext(map, brief: true);
            return section;
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
                var spawnedList = map.listerThings?.ThingsOfDef(def)?.ToArray();
                if (spawnedList != null && spawnedList.Length > 0)
                {
                    int unforbiddenTotal = 0;
                    for (int i = 0; i < spawnedList.Length; i++)
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
            catch { }
            return counted;
        }
    }
}
