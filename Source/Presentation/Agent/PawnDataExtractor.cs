using System.Collections.Generic;
using System.Linq;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Context;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Models.Context;
using RimWorld;
using UnityEngine;
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
            try
            {
                result.Temperature = UnityData.IsInMainThread
                    ? pawn.AmbientTemperature
                    : (pawn.Map?.mapTemperature?.OutdoorTemp ?? 21f);
            }
            catch { result.Temperature = 21f; }
            result.Name = pawn.Name?.ToStringFull ?? "";
            result.Gender = pawn.gender.ToString();
            try
            {
                result.GenderLabel = UnityData.IsInMainThread ? pawn.gender.GetLabel() : pawn.gender.ToString();
            }
            catch { result.GenderLabel = pawn.gender.ToString(); }
            result.AgeBiological = (int)(pawn.ageTracker?.AgeBiologicalTicks ?? 0);
            result.Age = pawn.ageTracker?.AgeBiologicalYears.ToString() ?? "0";
            result.Race = pawn.def?.defName ?? "";
            result.RaceLabel = pawn.def?.label ?? "";
            try
            {
                result.Title = UnityData.IsInMainThread ? (pawn.royalty?.MainTitle()?.label ?? "") : "";
            }
            catch { result.Title = ""; }
            result.Faction = pawn.Faction?.Name ?? "";
            result.IdeologyName = pawn.Ideo?.name ?? "";
            try
            {
                var memes = pawn.Ideo?.memes?.ToArray();
                result.IdeologyMemes = memes != null && memes.Length > 0
                    ? string.Join(", ", memes.Where(m => m != null).Select(m => m.label ?? m.defName ?? ""))
                    : "";
            }
            catch { result.IdeologyMemes = ""; }
            result.ChildhoodTitle = pawn.story?.Childhood?.title ?? "";
            result.AdulthoodTitle = pawn.story?.Adulthood?.title ?? "";
            result.Skills = new Dictionary<string, int>();
            result.Traits = new List<string>();
            try
            {
                var rawTraits = pawn.story?.traits?.allTraits?.ToArray();
                if (rawTraits != null && rawTraits.Length > 0)
                {
                    foreach (var t in rawTraits)
                    {
                        if (t == null) continue;
                        string tLabel = UnityData.IsInMainThread
                            ? t.Label
                            : (t.CurrentData?.label ?? t.def?.defName ?? "");
                        if (!string.IsNullOrEmpty(tLabel))
                            result.Traits.Add(tLabel);
                    }
                }
                result.TraitLabels = result.Traits.Count > 0 ? string.Join(", ", result.Traits) : "";
            }
            catch { result.TraitLabels = ""; }
            result.Relations = new List<RelationEntry>();
            result.HealthSummary = "";
            result.EquippedWeapon = pawn.equipment?.Primary?.def?.label ?? pawn.equipment?.Primary?.def?.defName ?? "";
            result.WeaponLabel = result.EquippedWeapon;
            result.Drafted = pawn.Drafted;
            result.EnemyTargetLabel = "";
            result.EnemyTargetHpPercent = 0f;
            try
            {
                result.RoomLabel = UnityData.IsInMainThread
                    ? (pawn.GetRoom()?.Role?.label ?? "")
                    : "";
            }
            catch { result.RoomLabel = ""; }
            result.WeatherLabel = pawn.Map?.weatherManager?.curWeather?.label ?? "";
            result.TimeString = "";
            result.NearbyPawnNames = "";
            try
            {
                if (pawn.Map != null && pawn.Spawned)
                {
                    var colonists = RimMind.Presentation.Api.RimMindPawnLookup.GetSpawnedColonistsSafe(pawn.Map);
                    var nearby = new List<string>();
                    for (int i = 0; i < colonists.Count; i++)
                    {
                        var other = colonists[i];
                        if (other != null && other != pawn && other.Position.InHorDistOf(pawn.Position, 15f))
                        {
                            string n = other.Name?.ToStringShort ?? other.LabelShort ?? "";
                            if (!string.IsNullOrEmpty(n))
                                nearby.Add(n);
                        }
                    }
                    if (nearby.Count > 0)
                        result.NearbyPawnNames = string.Join(", ", nearby);
                }
            }
            catch { result.NearbyPawnNames = ""; }
            result.SeasonLabel = "";
            try
            {
                if (pawn.Map != null)
                {
                    var conds = pawn.Map.gameConditionManager?.ActiveConditions?.ToArray();
                    if (conds != null && conds.Length > 0)
                        result.SeasonLabel = string.Join(", ", conds.Where(c => c?.def != null).Select(c => c.def.label ?? c.def.defName ?? ""));
                }
            }
            catch (System.Exception ex) { logSink?.Warning($"PawnDataExtractor: SeasonLabel failed for {pawn.Name}: {ex.Message}"); }
            result.ColonistCount = pawn.Map != null
                ? (UnityData.IsInMainThread
                    ? pawn.Map.mapPawns?.FreeColonistsCount ?? 0
                    : RimMind.Presentation.Api.RimMindPawnLookup.GetSpawnedColonistsSafe(pawn.Map).Count)
                : 0;
            try
            {
                result.ColonyWealth = UnityData.IsInMainThread
                    ? (pawn.Map?.wealthWatcher?.WealthTotal ?? 0f)
                    : 0f;
            }
            catch { result.ColonyWealth = 0f; }
            result.ThreatCount = 0;
            result.Hediffs = new List<HediffEntry>();
            result.InMentalState = pawn.MentalStateDef != null;
            result.MentalStateInspectLine = pawn.MentalStateDef != null ? (pawn.MentalStateDef.label ?? pawn.MentalStateDef.defName) : "";
            string jobReport = "";
            try
            {
                if (UnityData.IsInMainThread)
                {
                    jobReport = pawn.jobs?.curDriver?.GetReport()?.ToString() ?? "";
                }
                else if (pawn.jobs?.curJob?.def != null)
                {
                    string rawReport = pawn.jobs.curJob.def.reportString ?? pawn.jobs.curJob.def.label ?? pawn.jobs.curJob.def.defName;
                    string targetA = pawn.jobs.curJob.targetA.Thing?.def?.label ?? "";
                    jobReport = !string.IsNullOrEmpty(targetA)
                        ? rawReport.Replace("TargetA.", targetA).Replace("TargetA", targetA)
                        : rawReport;
                }
            }
            catch { }
            result.CurrentJobReport = jobReport;
            result.CurrentJobDefLabel = pawn.jobs?.curJob?.def?.label ?? "";
            result.Downed = pawn.Downed;
            result.InCombat = pawn.MentalStateDef != null;

            result.NotableGenes = new List<string>();
            try
            {
                var genesSnapshot = pawn.genes?.GenesListForReading?.ToArray();
                if (genesSnapshot != null)
                    foreach (var gene in genesSnapshot)
                        if (gene != null && gene.def != null)
                            result.NotableGenes.Add(gene.def.label ?? gene.def.defName);
            }
            catch (System.Exception ex) { logSink?.Warning($"PawnDataExtractor: Genes failed for {pawn.Name}: {ex.Message}"); }

            result.MoodThoughts = new List<MoodThoughtEntry>();
            try
            {
                if (pawn.needs?.mood?.thoughts != null)
                {
                    var allThoughts = new List<Thought>();
                    if (UnityData.IsInMainThread)
                    {
                        pawn.needs.mood.thoughts.GetAllMoodThoughts(allThoughts);
                    }
                    else if (pawn.needs.mood.thoughts.memories?.Memories != null)
                    {
                        var memSnapshot = pawn.needs.mood.thoughts.memories.Memories.ToArray();
                        for (int i = 0; i < memSnapshot.Length; i++)
                        {
                            if (memSnapshot[i] != null)
                                allThoughts.Add(memSnapshot[i]);
                        }
                    }

                    foreach (var thought in allThoughts)
                    {
                        if (thought == null) continue;
                        float offset = 0f;
                        try
                        {
                            offset = UnityData.IsInMainThread
                                ? thought.MoodOffset()
                                : (thought.CurStage?.baseMoodEffect ?? 0f);
                        }
                        catch { continue; }

                        if (Mathf.Abs(offset) > 0.01f)
                        {
                            string label = "";
                            if (UnityData.IsInMainThread)
                            {
                                try { label = thought.LabelCap; } catch { }
                            }
                            if (string.IsNullOrEmpty(label))
                            {
                                label = thought.CurStage?.label ?? thought.def?.label ?? "";
                                if (thought is Thought_MemorySocial social && social.otherPawn != null && label.Contains("{0}"))
                                    label = label.Replace("{0}", social.otherPawn.Name?.ToStringShort ?? "");
                            }

                            label = SanitizeThoughtLabel(label);
                            if (string.IsNullOrWhiteSpace(label))
                                continue;

                            result.MoodThoughts.Add(new MoodThoughtEntry
                            {
                                Label = label,
                                Offset = offset
                            });
                        }
                    }
                    result.MoodThoughts.Sort((a, b) => Mathf.Abs(b.Offset).CompareTo(Mathf.Abs(a.Offset)));
                }
            }
            catch (System.Exception ex) { logSink?.Warning($"PawnDataExtractor: MoodThoughts failed for {pawn.Name}: {ex.Message}"); }

            result.Capacities = new List<CapacityEntry>();
            try
            {
                if (UnityData.IsInMainThread && pawn.health?.capacities != null)
                {
                    foreach (PawnCapacityDef capDef in DefDatabase<PawnCapacityDef>.AllDefsListForReading)
                    {
                        if (capDef != null && pawn.health.capacities.GetLevel(capDef) is float level)
                            result.Capacities.Add(new CapacityEntry { Label = capDef.label ?? capDef.defName, Level = level });
                    }
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
                var wornSnapshot = pawn.apparel?.WornApparel?.ToArray();
                if (wornSnapshot != null)
                    foreach (var apparel in wornSnapshot)
                        if (apparel != null)
                            result.ApparelLabels.Add(apparel.def?.label ?? apparel.def?.defName ?? "");
            }
            catch (System.Exception ex) { logSink?.Warning($"PawnDataExtractor: Apparel failed for {pawn.Name}: {ex.Message}"); }

            result.InventoryItems = new Dictionary<string, int>();
            try
            {
                var invSnapshot = pawn.inventory?.innerContainer?.ToArray();
                if (invSnapshot != null)
                    foreach (var thing in invSnapshot)
                        if (thing != null)
                        {
                            string key = thing.def?.defName ?? "";
                            int count = thing.stackCount;
                            if (result.InventoryItems.ContainsKey(key))
                                result.InventoryItems[key] += count;
                            else
                                result.InventoryItems[key] = count;
                        }
            }
            catch (System.Exception ex) { logSink?.Warning($"PawnDataExtractor: Inventory failed for {pawn.Name}: {ex.Message}"); }

            try
            {
                var skillsSnapshot = pawn.skills?.skills?.ToArray();
                if (skillsSnapshot != null)
                    foreach (var s in skillsSnapshot)
                        if (s?.def != null) result.Skills[s.def.defName] = s.Level;
            }
            catch { }

            try
            {
                var hediffsSnapshot = pawn.health?.hediffSet?.hediffs?.ToArray();
                if (hediffsSnapshot != null)
                    foreach (var h in hediffsSnapshot)
                        if (h != null)
                            result.Hediffs.Add(new HediffEntry
                            {
                                HediffLabel = UnityData.IsInMainThread ? h.Label : (h.def?.label ?? h.def?.defName ?? ""),
                                Visible = h.Visible,
                                IsBad = h.def?.stages?.Any(st => st?.painOffset > 0) == true,
                                Severity = h.Severity,
                                PartLabel = h.Part?.customLabel ?? h.Part?.def?.label
                            });
            }
            catch { }

            try
            {
                var relSnapshot = pawn.relations?.DirectRelations?.ToArray();
                if (relSnapshot != null)
                    foreach (var rel in relSnapshot)
                        if (rel != null && rel.otherPawn != null)
                        {
                            string otherName = rel.otherPawn.Name?.ToStringShort ?? "";
                            if (rel.otherPawn.Dead)
                                otherName += " [Deceased]";
                            else if (!rel.otherPawn.Spawned || rel.otherPawn.Map == null)
                                otherName += " [Lost/Off-map]";

                            result.Relations.Add(new RelationEntry
                            {
                                RelationLabel = rel.def?.label ?? "",
                                OtherName = otherName
                            });
                        }
            }
            catch { }

            return result;
        }

        internal static string SanitizeThoughtLabel(string? label)
        {
            if (string.IsNullOrWhiteSpace(label)) return string.Empty;
            string cleaned = label!.Replace("{ROLE_label}", "ideology role")
                                   .Replace("{ROLE_labelDef}", "ideology role")
                                   .Replace("{IDEO}", "ideology")
                                   .Replace("{IDEO_name}", "ideology");
            if (cleaned.IndexOf('{') >= 0 && cleaned.IndexOf('}') > cleaned.IndexOf('{'))
            {
                cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\{[A-Za-z0-9_]+\}", "").Trim();
                cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s{2,}", " ").Trim();
            }
            return cleaned;
        }
    }
}
