using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Models;
using RimMind.Application.Common.Models.Prompt;
using RimMind.Application.Features.Prompt;
using RimMind.Domain.ValueObjects;
using RimMind.Presentation.Agent;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimMind.Presentation.Agent
{
    /// <summary>
    /// Responsible for building Pawn-related context strings.
    /// Extracted from GameContextBuilder to satisfy SRP.
    /// </summary>
    public sealed partial class PawnContextBuilder
    {
        private const float HediffSeverityFilter = RimMindDefaults.HediffSeverityFilter;
        private const int MaxHealthIssues = 8;
        private const int MaxItemsDisplay = 8;
        private const int CompactHealthThreshold = 3;
        private const int MaxBuildingDisplay = 5;
        private const int MaxAnimalDisplay = 4;
        private const int MaxSkillDisplay = 5;
        private const int MaxHealthTagDisplay = 5;

        private static readonly IContextSettings DefaultFallbackSettings = new RimMind.Presentation.Settings.ContextSettings();

        private readonly IContextSettings? _contextSettings;
        private readonly ILogSink? _logSink;

        public PawnContextBuilder(IContextSettings? contextSettings = null, ILogSink? logSink = null)
        {
            _contextSettings = contextSettings;
            _logSink = logSink;
        }

        private IPawnIncludeSettings PawnSettings => _contextSettings ?? DefaultFallbackSettings;

        public string BuildPawnContext(Pawn pawn)
        {
            if (pawn == null) return string.Empty;

            var data = PawnDataExtractor.Extract(pawn, _logSink);
            var ctx = PawnSettings;
            var sb = new StringBuilder();
            sb.Append("RimMind.Prompt.PawnStatusHeader".Translate(data.Name) + "  ");

            var basics = new List<string>();
            if (ctx.IncludeAge) basics.Add("RimMind.Prompt.AgeFormat".Translate(data.Age));
            if (ctx.IncludeGender) basics.Add(data.GenderLabel);
            if (ctx.IncludeRace) basics.Add(data.RaceLabel);
            if (basics.Count > 0) sb.AppendLine(string.Join("  ", basics));
            else sb.AppendLine();

            if (ctx.IncludeGenes && data.NotableGenes.Count > 0)
                sb.AppendLine("RimMind.Prompt.Genes".Translate(string.Join(", ", data.NotableGenes)));

            if (ctx.IncludeBackstory && (data.ChildhoodTitle != null || data.AdulthoodTitle != null))
            {
                var parts = new List<string>();
                if (data.ChildhoodTitle != null)
                    parts.Add("RimMind.Prompt.Childhood".Translate(data.ChildhoodTitle));
                if (data.AdulthoodTitle != null)
                    parts.Add("RimMind.Prompt.Adulthood".Translate(data.AdulthoodTitle));
                if (parts.Count > 0)
                    sb.AppendLine("RimMind.Prompt.Backstory".Translate(string.Join("  ", parts)));
            }

            if (ctx.IncludeIdeology && data.IdeologyName != null)
                sb.AppendLine("RimMind.Prompt.IdeologyFormat".Translate(data.IdeologyName, data.IdeologyMemes));

            if (ctx.IncludeMood && data.MoodString != null)
            {
                if (data.InMentalState)
                    sb.AppendLine("RimMind.Prompt.MoodBreak".Translate(data.MoodString, data.MentalStateInspectLine));
                else if (data.Downed)
                    sb.AppendLine("RimMind.Prompt.MoodDowned".Translate(data.MoodString));
                else
                    sb.AppendLine("RimMind.Prompt.MoodPercent".Translate(data.MoodString, $"{data.MoodPercent:F0}"));
            }

            if (ctx.IncludeMoodThoughts && data.MoodThoughts.Count > 0)
            {
                var factors = data.MoodThoughts.Select(t => $"{t.Label}({t.Offset:+0;-0})");
                sb.AppendLine("RimMind.Prompt.MoodFactors".Translate(string.Join(", ", factors)));
            }

            if (ctx.IncludeHealth && data.Hediffs.Count > 0)
            {
                var notable = new List<string>();
                foreach (var h in data.Hediffs)
                {
                    if (!h.IsBad || h.Severity < HediffSeverityFilter || !h.Visible) continue;
                    string partLabel = h.PartLabel ?? "RimMind.Prompt.FullBody".Translate();
                    notable.Add($"{partLabel}: {h.HediffLabel}");
                }
                if (notable.Count > 0)
                    sb.AppendLine("RimMind.Prompt.HealthIssues".Translate(string.Join(", ", notable.Take(MaxHealthIssues))));
            }

            if (ctx.IncludeCapacities && data.Capacities.Count > 0)
            {
                var low = data.Capacities.Select(c => $"{c.Label}{c.Level * 100f:F0}%");
                sb.AppendLine("RimMind.Prompt.Capacities".Translate(string.Join(", ", low)));
            }

            if (ctx.IncludeSkills && data.Skills.Count > 0)
            {
                var skills = data.Skills
                    .Where(s => s.Value >= ctx.MinSkillLevel)
                    .Select(s => $"{s.Key}({s.Value})")
                    .ToList();
                if (skills.Count > 0)
                    sb.AppendLine("RimMind.Prompt.Skills".Translate(string.Join("  ", skills)));
            }

            if (ctx.IncludeCurrentJob)
            {
                string jobLabel = data.CurrentJobReport
                    ?? data.CurrentJobDefLabel
                    ?? "RimMind.Prompt.None".Translate();
                sb.AppendLine("RimMind.Prompt.CurrentJob".Translate(jobLabel));
            }

            if (ctx.IncludeWorkPriorities && data.WorkPriorities.Count > 0)
            {
                sb.AppendLine("RimMind.Prompt.WorkPriorities".Translate(
                    string.Join("  ", data.WorkPriorities.Select(e => $"{e.Label}({e.Priority})"))));
            }

            if (ctx.IncludeTraits && !string.IsNullOrEmpty(data.TraitLabels))
                sb.AppendLine("RimMind.Prompt.Traits".Translate(data.TraitLabels));

            if (ctx.IncludeEquipment)
            {
                var parts = new List<string>();
                if (data.WeaponLabel != null)
                    parts.Add("RimMind.Prompt.Weapon".Translate(data.WeaponLabel));
                if (data.ApparelLabels.Count > 0)
                    parts.Add("RimMind.Prompt.Apparel".Translate(string.Join(", ", data.ApparelLabels)));
                if (parts.Count > 0)
                    sb.AppendLine(string.Join("  ", parts));
            }

            if (ctx.IncludeInventory && data.InventoryItems.Count > 0)
            {
                var itemStrs = data.InventoryItems.OrderByDescending(kv => kv.Value)
                    .Take(MaxItemsDisplay)
                    .Select(kv =>
                    {
                        var def = DefDatabase<ThingDef>.GetNamedSilentFail(kv.Key);
                        string label = def?.LabelCap ?? kv.Key;
                        return kv.Value > 1 ? $"{label}×{kv.Value}" : label;
                    });
                sb.AppendLine("RimMind.Prompt.Inventory".Translate(string.Join(", ", itemStrs)));
            }

            if (ctx.IncludeLocation && data.HasMap)
            {
                int temp = Mathf.RoundToInt(data.Temperature);
                sb.AppendLine("RimMind.Prompt.Location".Translate(data.RoomLabel, $"{temp}"));
            }

            if (ctx.IncludeRelations && data.Relations.Count > 0)
            {
                var relParts = data.Relations.Select(r => $"{r.RelationLabel}({r.OtherName})");
                sb.AppendLine("RimMind.Prompt.Relations".Translate(string.Join(", ", relParts)));
            }

            if (ctx.IncludeCombatStatus)
            {
                if (data.InCombat)
                {
                    string targetLabel = data.EnemyTargetLabel ?? "RimMind.Prompt.Unknown".Translate();
                    sb.AppendLine("RimMind.Prompt.InCombat".Translate(targetLabel));
                }
                if (data.Drafted)
                    sb.AppendLine("RimMind.Prompt.Drafted".Translate());
            }

            if (ctx.IncludeSurroundings && pawn.Map != null)
            {
                string surroundings = BuildSurroundings(pawn);
                if (!string.IsNullOrEmpty(surroundings))
                    sb.AppendLine("RimMind.Prompt.Surroundings".Translate(surroundings));
            }

            return sb.ToString().TrimEnd();
        }

        public string BuildCompactPawnContext(Pawn pawn)
        {
            if (pawn == null) return string.Empty;

            var data = PawnDataExtractor.Extract(pawn, _logSink);
            var sb = new StringBuilder();
            sb.Append(data.Name + "  ");

            var basics = new List<string>();
            basics.Add("RimMind.Prompt.AgeFormat".Translate(data.Age));
            basics.Add(data.GenderLabel);
            basics.Add(data.RaceLabel);
            sb.AppendLine(string.Join("  ", basics));

            if (data.MoodString != null)
            {
                string moodLabel = data.InMentalState ? "RimMind.Prompt.CompactMentalBreak".Translate()
                    : data.Downed ? "RimMind.Prompt.CompactDowned".Translate()
                    : $"{data.MoodPercent:F0}%";
                sb.AppendLine("RimMind.Prompt.CompactMood".Translate(moodLabel));
            }

            if (data.Hediffs.Count > 0)
            {
                var notable = new List<string>();
                foreach (var h in data.Hediffs)
                {
                    if (!h.IsBad || h.Severity < HediffSeverityFilter || !h.Visible) continue;
                    string partLabel = h.PartLabel ?? "RimMind.Prompt.FullBody".Translate();
                    notable.Add($"{partLabel}:{h.HediffLabel}");
                    if (notable.Count >= CompactHealthThreshold) break;
                }
                if (notable.Count > 0)
                    sb.AppendLine("RimMind.Prompt.CompactHealth".Translate(string.Join(", ", notable)));
            }

            string jobLabel = data.CurrentJobReport
                ?? data.CurrentJobDefLabel
                ?? "RimMind.Prompt.None".Translate();
            sb.AppendLine("RimMind.Prompt.CompactJob".Translate(jobLabel));

            if (data.HasMap)
            {
                int temp = Mathf.RoundToInt(data.Temperature);
                sb.AppendLine("RimMind.Prompt.CompactLocation".Translate(data.RoomLabel, $"{temp}"));
            }

            if (data.WeaponLabel != null)
                sb.AppendLine("RimMind.Prompt.CompactWeapon".Translate(data.WeaponLabel));

            if (data.Drafted)
                sb.AppendLine("RimMind.Prompt.Drafted".Translate());
            if (data.EnemyTargetLabel != null)
                sb.AppendLine("RimMind.Prompt.InCombat".Translate(data.EnemyTargetLabel));

            return sb.ToString().TrimEnd();
        }

        public PromptSection BuildPawnContextSection(Pawn pawn)
        {
            var section = new PromptSection("pawn_context", BuildPawnContext(pawn), PromptSection.PriorityKeyState);
            section.Compress = _ => BuildCompactPawnContext(pawn);
            return section;
        }

        public PromptSection BuildCompactPawnContextSection(Pawn pawn)
        {
            return new PromptSection("pawn_compact", BuildCompactPawnContext(pawn), PromptSection.PriorityKeyState);
        }

        private string BuildSurroundings(Pawn pawn, int? radius = null, int? maxItems = null)
        {
            var envSettings = _contextSettings as IContextEnvironmentSettings;
            int r = radius ?? (envSettings?.EnvironmentScanRadius ?? 5);
            int m = maxItems ?? (envSettings?.EnvironmentMaxItems ?? 8);
            var map = pawn.Map;
            var buildings = new List<string>();
            var items = new Dictionary<string, int>();
            var animals = new List<string>();

            foreach (var c in GenRadial.RadialCellsAround(pawn.Position, r, true))
            {
                if (!c.InBounds(map)) continue;
                var room = pawn.GetRoom();
                if (room != null && !room.PsychologicallyOutdoors)
                {
                    var cRoom = c.GetRoom(map);
                    if (cRoom != room) continue;
                }

                var things = c.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    var thing = things[i];
                    if (thing.DestroyedOrNull() || thing == pawn) continue;

                    if (thing is Pawn otherPawn)
                    {
                        if (otherPawn.RaceProps.Animal && !otherPawn.Dead)
                            animals.Add(otherPawn.LabelShort ?? otherPawn.def.label);
                        continue;
                    }

                    if (thing.def.category == ThingCategory.Building)
                    {
                        if (buildings.Count < MaxBuildingDisplay)
                            buildings.Add(thing.def.LabelCap);
                    }
                    else if (thing.def.category == ThingCategory.Item)
                    {
                        string key = thing.def.defName;
                        if (!items.ContainsKey(key))
                            items[key] = 0;
                        items[key] += thing.stackCount;
                    }
                }
            }

            var parts = new List<string>();
            if (buildings.Count > 0)
                parts.Add("RimMind.Prompt.SurroundingsBuildings".Translate(string.Join(", ", buildings.Distinct().Take(MaxBuildingDisplay))));
            if (items.Count > 0)
            {
                var itemStrs = items.OrderByDescending(kv => kv.Value)
                    .Take(m)
                    .Select(kv => $"{DefDatabase<ThingDef>.GetNamedSilentFail(kv.Key)?.LabelCap ?? kv.Key}×{kv.Value}");
                parts.Add("RimMind.Prompt.SurroundingsItems".Translate(string.Join(", ", itemStrs)));
            }
            if (animals.Count > 0)
                parts.Add("RimMind.Prompt.SurroundingsAnimals".Translate(string.Join(", ", animals.Distinct().Take(MaxAnimalDisplay))));

            return parts.Count > 0 ? string.Join("  ", parts) : string.Empty;
        }

    }
}
