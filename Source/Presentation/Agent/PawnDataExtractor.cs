using System.Collections.Generic;
using System.Linq;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Context;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Models.Context;
using Verse;

namespace RimMind.Presentation.Agent
{
    public static class PawnDataExtractor
    {
        public static PawnExtractedData Extract(Pawn pawn, ILogSink? logSink)
        {
            var result = new PawnExtractedData();
            if (pawn == null) return result;

            result.MoodString = pawn.needs?.mood?.CurLevel.ToString("F1") ?? "0.0";
            result.MoodPercent = pawn.needs?.mood?.CurLevel ?? 0f;
            result.HasMap = pawn.Map != null;
            result.Temperature = pawn.AmbientTemperature;
            result.Name = pawn.Name?.ToStringFull ?? "";
            result.Gender = pawn.gender.ToString();
            result.GenderLabel = pawn.gender.GetLabel();
            result.AgeBiological = (int)(pawn.ageTracker?.AgeBiologicalTicks ?? 0);
            result.Age = pawn.ageTracker?.AgeBiologicalYears.ToString() ?? "0";
            result.Race = pawn.def?.defName ?? "";
            result.RaceLabel = pawn.def?.label ?? "";
            result.Title = pawn.royalty?.MainTitle()?.label ?? "";
            result.Faction = pawn.Faction?.Name ?? "";
            result.IdeologyName = pawn.Ideo?.name ?? "";
            result.IdeologyMemes = pawn.Ideo?.memes?.Select(m => m.label)?.ToCommaList() ?? "";
            result.ChildhoodTitle = pawn.story?.Childhood?.title ?? "";
            result.AdulthoodTitle = pawn.story?.Adulthood?.title ?? "";
            result.Skills = new Dictionary<string, int>();
            result.Traits = new List<string>();
            result.TraitLabels = pawn.story?.traits?.allTraits?.Select(t => t.Label)?.ToCommaList() ?? "";
            result.Relations = new List<RelationEntry>();
            result.HealthSummary = "";
            result.EquippedWeapon = pawn.equipment?.Primary?.Label ?? "";
            result.WeaponLabel = pawn.equipment?.Primary?.Label ?? "";
            result.Drafted = pawn.Drafted;
            result.EnemyTargetLabel = "";
            result.EnemyTargetHpPercent = 0f;
            result.RoomLabel = pawn.GetRoom()?.Role?.label ?? "";
            result.WeatherLabel = pawn.Map?.weatherManager?.curWeather?.label ?? "";
            result.TimeString = "";
            result.NearbyPawnNames = "";
            result.SeasonLabel = "";
            try
            {
                if (pawn.Map != null)
                    result.SeasonLabel = pawn.Map.gameConditionManager?.ActiveConditions?.Select(c => c.Label)?.ToCommaList() ?? "";
            }
            catch (System.Exception ex) { logSink?.Warning($"PawnDataExtractor: SeasonLabel failed for {pawn.Name}: {ex.Message}"); }
            result.ColonistCount = pawn.Map?.mapPawns?.FreeColonistsCount ?? 0;
            result.ColonyWealth = pawn.Map?.wealthWatcher?.WealthTotal ?? 0f;
            result.ThreatCount = 0;
            result.Hediffs = new List<HediffEntry>();
            result.InMentalState = pawn.MentalStateDef != null;
            result.MentalStateInspectLine = pawn.MentalStateDef != null ? pawn.MentalStateDef.LabelCap : "";
            result.CurrentJobReport = pawn.jobs?.curDriver?.GetReport()?.ToString() ?? "";
            result.CurrentJobDefLabel = pawn.jobs?.curJob?.def?.label ?? "";
            result.Downed = pawn.Downed;
            result.InCombat = pawn.MentalStateDef != null;

            result.NotableGenes = new List<string>();
            try
            {
                if (pawn.genes?.GenesListForReading != null)
                    foreach (var gene in pawn.genes.GenesListForReading)
                        if (gene != null && gene.def != null)
                            result.NotableGenes.Add(gene.def.label ?? gene.def.defName);
            }
            catch (System.Exception ex) { logSink?.Warning($"PawnDataExtractor: Genes failed for {pawn.Name}: {ex.Message}"); }

            result.MoodThoughts = new List<MoodThoughtEntry>();
            try
            {
                if (pawn.needs?.mood?.thoughts?.memories != null)
                    foreach (var thought in pawn.needs.mood.thoughts.memories.Memories)
                        if (thought != null)
                            result.MoodThoughts.Add(new MoodThoughtEntry
                            {
                                Label = thought.def?.label ?? "",
                                Offset = thought.MoodOffset()
                            });
            }
            catch (System.Exception ex) { logSink?.Warning($"PawnDataExtractor: MoodThoughts failed for {pawn.Name}: {ex.Message}"); }

            result.Capacities = new List<CapacityEntry>();
            try
            {
                foreach (PawnCapacityDef capDef in DefDatabase<PawnCapacityDef>.AllDefsListForReading)
                {
                    if (capDef != null && pawn.health?.capacities?.GetLevel(capDef) is float level)
                        result.Capacities.Add(new CapacityEntry { Label = capDef.label ?? capDef.defName, Level = level });
                }
            }
            catch (System.Exception ex) { logSink?.Warning($"PawnDataExtractor: Capacities failed for {pawn.Name}: {ex.Message}"); }

            result.WorkPriorities = new List<WorkPriorityEntry>();
            try
            {
                if (pawn.workSettings != null)
                    foreach (WorkTypeDef wtd in DefDatabase<WorkTypeDef>.AllDefsListForReading)
                        if (wtd != null && pawn.workSettings.GetPriority(wtd) > 0)
                            result.WorkPriorities.Add(new WorkPriorityEntry { Label = wtd.labelShort ?? wtd.defName, Priority = pawn.workSettings.GetPriority(wtd) });
            }
            catch (System.Exception ex) { logSink?.Warning($"PawnDataExtractor: WorkPriorities failed for {pawn.Name}: {ex.Message}"); }

            result.ApparelLabels = new List<string>();
            try
            {
                if (pawn.apparel?.WornApparel != null)
                    foreach (var apparel in pawn.apparel.WornApparel)
                        if (apparel != null)
                            result.ApparelLabels.Add(apparel.Label ?? apparel.def?.label ?? "");
            }
            catch (System.Exception ex) { logSink?.Warning($"PawnDataExtractor: Apparel failed for {pawn.Name}: {ex.Message}"); }

            result.InventoryItems = new Dictionary<string, int>();
            try
            {
                if (pawn.inventory?.innerContainer != null)
                    foreach (var thing in pawn.inventory.innerContainer)
                        if (thing != null)
                        {
                            string key = thing.def?.defName ?? thing.Label;
                            int count = thing.stackCount;
                            if (result.InventoryItems.ContainsKey(key))
                                result.InventoryItems[key] += count;
                            else
                                result.InventoryItems[key] = count;
                        }
            }
            catch (System.Exception ex) { logSink?.Warning($"PawnDataExtractor: Inventory failed for {pawn.Name}: {ex.Message}"); }

            if (pawn.skills?.skills != null)
                foreach (var s in pawn.skills.skills)
                    if (s != null) result.Skills[s.def.defName] = s.Level;

            if (pawn.story?.traits?.allTraits != null)
                foreach (var t in pawn.story.traits.allTraits)
                    if (t != null) result.Traits.Add(t.Label);

            if (pawn.health?.hediffSet?.hediffs != null)
                foreach (var h in pawn.health.hediffSet.hediffs)
                    if (h != null)
                        result.Hediffs.Add(new HediffEntry
                        {
                            HediffLabel = h.Label,
                            Visible = h.Visible,
                            IsBad = h.def?.stages?.Any(st => st?.painOffset > 0) == true,
                            Severity = h.Severity,
                            PartLabel = h.Part?.Label
                        });

            if (pawn.relations?.DirectRelations != null)
                foreach (var rel in pawn.relations.DirectRelations)
                    if (rel != null)
                        result.Relations.Add(new RelationEntry
                        {
                            RelationLabel = rel.def?.label ?? "",
                            OtherName = rel.otherPawn?.Name?.ToStringShort ?? ""
                        });

            return result;
        }
    }
}
