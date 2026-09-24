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

            entries.Add(new ContextEntry("RimMind.Prompt.MapStatusHeader".Translate()));

            if (ctx.IncludeGameTime)
            {
                long ticks = Find.TickManager.TicksAbs;
                Vector2 longLat = Find.WorldGrid.LongLatOf(map.Tile);
                int hour = GenDate.HourOfDay(ticks, longLat.x);
                string dateStr = GenDate.DateFullStringAt(ticks, longLat);
                int day = (int)(ticks / TicksPerDay);
                entries.Add(new ContextEntry(
                    "RimMind.Prompt.TimeFormat".Translate(dateStr, $"{hour:D2}"))
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
                var colonists = map.mapPawns.FreeColonistsSpawned;
                int count = colonists.Count;
                string content;
                if (colony.IncludeColonistNames && colonists.Count > 0)
                {
                    var names = colonists.Select(p => p.Name.ToStringShort);
                    string nameList = string.Join(", ", names);
                    content = "RimMind.Prompt.ColonistCount".Translate(count, nameList);
                }
                else
                {
                    content = "RimMind.Prompt.ColonistCountBrief".Translate(count);
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
                var prisoners = map.mapPawns.PrisonersOfColonySpawned;
                if (prisoners.Count > 0)
                {
                    var names = prisoners.Select(p => p.Name.ToStringShort);
                    string nameList = string.Join(", ", names);
                    otherSb.AppendLine("RimMind.Prompt.PrisonerCount".Translate(prisoners.Count, nameList));
                }
            }

            if (colony.IncludeWealth)
            {
                float wealth = map.wealthWatcher.WealthTotal;
                string threat = ThreatLabel(wealth);
                otherSb.AppendLine("RimMind.Prompt.WealthWithThreat".Translate($"{wealth:F0}", threat));
            }

            if (colony.IncludeThreats)
            {
                float wealth = map.wealthWatcher.WealthTotal;
                string threat = ThreatLabel(wealth);
                otherSb.AppendLine("RimMind.Prompt.ThreatLevel".Translate(threat));
            }

            if (colony.IncludeFood)
            {
                float foodNutrition = 0f;
                for (int i = 0; i < DefDatabase<ThingDef>.AllDefsListForReading.Count; i++)
                {
                    var def = DefDatabase<ThingDef>.AllDefsListForReading[i];
                    if (def.IsNutritionGivingIngestible && def.ingestible != null
                        && def.ingestible.HumanEdible && !def.IsCorpse)
                    {
                        foodNutrition += map.resourceCounter.GetCount(def) * def.ingestible.CachedNutrition;
                    }
                }
                otherSb.AppendLine("RimMind.Prompt.FoodStorage".Translate($"{foodNutrition:F0}"));
            }

            if (ctx.IncludeSeason)
                otherSb.Append("RimMind.Prompt.Season".Translate(GenLocalDate.Season(map).Label()));
            if (ctx.IncludeWeather)
                otherSb.AppendLine("RimMind.Prompt.Weather".Translate(map.weatherManager.curWeather.label));
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
                "Extreme" => "RimMind.Prompt.Threat.Extreme".Translate(),
                "High"    => "RimMind.Prompt.Threat.High".Translate(),
                "Medium"  => "RimMind.Prompt.Threat.Medium".Translate(),
                _         => "RimMind.Prompt.Threat.Low".Translate()
            };
        }
    }
}
